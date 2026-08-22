using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace ProxyManager.Proxy;

/// <summary>A single proxied request captured by <see cref="TrafficMonitorMiddleware"/>.</summary>
public sealed record RequestSample(
    DateTimeOffset Timestamp,
    string Host,
    string Method,
    string Path,
    int StatusCode,
    long DurationMs,
    long BytesIn,
    long BytesOut,
    string? ClientIp,
    string? Error,
    string? RequestBody,
    string? ResponseBody);

/// <summary>Aggregated traffic for one hostname over a window.</summary>
public sealed record HostTrafficSummary(
    string Host,
    long Requests,
    long Failed,
    long Active,
    long BytesIn,
    long BytesOut,
    double AverageMs,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    long Class2xx,
    long Class3xx,
    long Class4xx,
    long Class5xx,
    long ClassOther,
    string? LastError,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen);

/// <summary>Process-wide totals for the diagnostics overview.</summary>
public sealed record TrafficOverview(
    DateTimeOffset StartedAt,
    long TotalRequests,
    long TotalFailed,
    int TrackedHosts,
    int BufferedSamples);

/// <summary>
/// In-memory per-hostname request statistics for the proxy port: rolling windows, a
/// bounded ring buffer of recent requests, and a Prometheus-visible meter
/// (<c>ProxyManager.Traffic</c> — <c>proxy_manager_traffic_*</c>). Fed by
/// <see cref="TrafficMonitorMiddleware"/>. Statistics are intentionally session-scoped
/// (like stream status): historical persistence is out of scope.
/// </summary>
public sealed class TrafficMonitor
{
    /// <summary>Duration histogram bucket upper bounds in milliseconds (last = infinity).</summary>
    private static readonly double[] BucketUpperMs =
        [5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10_000, double.MaxValue];

    private const int MaxSamples = 50_000;

    private readonly ConcurrentDictionary<string, HostTraffic> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<RequestSample> _samples = new();
    private readonly Meter _meter = new("ProxyManager.Traffic");
    private readonly Counter<long> _requests;
    private readonly Counter<long> _failed;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _bytes;
    private readonly UpDownCounter<long> _active;

    public TrafficMonitor()
    {
        _requests = _meter.CreateCounter<long>("traffic.requests",
            description: "Proxied requests by hostname and status class.");
        _failed = _meter.CreateCounter<long>("traffic.failed",
            description: "Failed proxied requests by hostname and reason (server_error | exception).");
        _duration = _meter.CreateHistogram<double>("traffic.duration", unit: "s",
            description: "Proxy request duration in seconds, by hostname.");
        _bytes = _meter.CreateCounter<long>("traffic.bytes", unit: "By",
            description: "Request/response bytes transferred by hostname and direction.");
        _active = _meter.CreateUpDownCounter<long>("traffic.active",
            description: "Requests currently being proxied, by hostname.");
        _meter.CreateObservableGauge("traffic.hosts", () => _hosts.Count,
            description: "Number of hostnames with tracked traffic.");
    }

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>Increments the active-request gauge for a hostname (proxy middleware).</summary>
    public void StartRequest(string host)
    {
        var traffic = _hosts.GetOrAdd(host, _ => new HostTraffic());
        Interlocked.Increment(ref traffic.Active);
        _active.Add(1, new KeyValuePair<string, object?>("host", host));
    }

    /// <summary>Decrements the active-request gauge for a hostname (proxy middleware).</summary>
    public void EndRequest(string host)
    {
        if (_hosts.TryGetValue(host, out var traffic))
        {
            Interlocked.Decrement(ref traffic.Active);
        }

        _active.Add(-1, new KeyValuePair<string, object?>("host", host));
    }

    /// <summary>Records one completed proxied request.</summary>
    public void Record(RequestSample sample)
    {
        var traffic = _hosts.GetOrAdd(sample.Host, _ => new HostTraffic { FirstSeen = sample.Timestamp });
        if (traffic.FirstSeen == default)
        {
            traffic.FirstSeen = sample.Timestamp;
        }

        Interlocked.Increment(ref traffic.Requests);
        Interlocked.Add(ref traffic.BytesIn, sample.BytesIn);
        Interlocked.Add(ref traffic.BytesOut, sample.BytesOut);
        Interlocked.Add(ref traffic.DurationSum, sample.DurationMs);
        IncrementClass(traffic, sample.StatusCode);
        Interlocked.Increment(ref traffic.Buckets[BucketIndex(sample.DurationMs)]);

        if (sample.Error is not null)
        {
            Interlocked.Increment(ref traffic.Failed);
            traffic.LastError = Truncate(sample.Error, 500);
        }

        traffic.LastSeen = sample.Timestamp;

        var hostTag = new KeyValuePair<string, object?>("host", sample.Host);
        _requests.Add(1, hostTag, new KeyValuePair<string, object?>("status_class", StatusClass(sample.StatusCode)));
        if (sample.Error is not null)
        {
            _failed.Add(1, hostTag,
                new KeyValuePair<string, object?>("reason", sample.StatusCode >= 500 ? "server_error" : "exception"));
        }

        _duration.Record(sample.DurationMs / 1000.0, hostTag);
        _bytes.Add(sample.BytesIn, hostTag, new KeyValuePair<string, object?>("direction", "in"));
        _bytes.Add(sample.BytesOut, hostTag, new KeyValuePair<string, object?>("direction", "out"));

        _samples.Enqueue(sample);
        while (_samples.Count > MaxSamples)
        {
            _samples.TryDequeue(out _);
        }
    }

    /// <summary>Per-hostname traffic for a window; <c>null</c> = all time since boot.</summary>
    public IReadOnlyList<HostTrafficSummary> Snapshot(TimeSpan? window)
    {
        if (window is not { } w)
        {
            return _hosts.Select(kv => ToSummary(kv.Key, kv.Value)).OrderByDescending(s => s.Requests).ToList();
        }

        var cutoff = DateTimeOffset.UtcNow - w;
        var accumulated = new Dictionary<string, HostTraffic>(StringComparer.OrdinalIgnoreCase);

        // The queue holds at most MaxSamples entries; scanning it per UI refresh is cheap.
        foreach (var sample in _samples)
        {
            if (sample.Timestamp < cutoff)
            {
                continue;
            }

            if (!accumulated.TryGetValue(sample.Host, out var acc))
            {
                acc = new HostTraffic();
                accumulated[sample.Host] = acc;
            }

            acc.Requests++;
            acc.BytesIn += sample.BytesIn;
            acc.BytesOut += sample.BytesOut;
            acc.DurationSum += sample.DurationMs;
            IncrementClass(acc, sample.StatusCode);
            acc.Buckets[BucketIndex(sample.DurationMs)]++;
            if (sample.Error is not null)
            {
                acc.Failed++;
                acc.LastError = Truncate(sample.Error, 500);
            }

            acc.FirstSeen = sample.Timestamp;
            acc.LastSeen = sample.Timestamp;
        }

        return accumulated
            .Select(kv =>
            {
                var live = _hosts.TryGetValue(kv.Key, out var h) ? h : null;
                return new HostTrafficSummary(
                    kv.Key,
                    kv.Value.Requests,
                    kv.Value.Failed,
                    live?.Active ?? 0,
                    kv.Value.BytesIn,
                    kv.Value.BytesOut,
                    Average(kv.Value),
                    Percentile(kv.Value.Buckets, kv.Value.Requests, 0.50),
                    Percentile(kv.Value.Buckets, kv.Value.Requests, 0.95),
                    Percentile(kv.Value.Buckets, kv.Value.Requests, 0.99),
                    kv.Value.Class2xx, kv.Value.Class3xx, kv.Value.Class4xx, kv.Value.Class5xx, kv.Value.ClassOther,
                    kv.Value.LastError,
                    kv.Value.FirstSeen,
                    kv.Value.LastSeen);
            })
            .OrderByDescending(s => s.Requests)
            .ToList();
    }

    /// <summary>The most recent requests (newest first), including captured bodies when capture is enabled.</summary>
    public IReadOnlyList<RequestSample> RecentRequests(int limit)
    {
        var samples = _samples.ToArray();
        var result = new List<RequestSample>(Math.Min(limit, samples.Length));
        for (var i = samples.Length - 1; i >= 0 && result.Count < limit; i--)
        {
            result.Add(samples[i]);
        }

        return result;
    }

    public TrafficOverview Overview() => new(
        StartedAt,
        _hosts.Values.Sum(h => Volatile.Read(ref h.Requests)),
        _hosts.Values.Sum(h => Volatile.Read(ref h.Failed)),
        _hosts.Count,
        _samples.Count);

    private static HostTrafficSummary ToSummary(string host, HostTraffic traffic) => new(
        host,
        Volatile.Read(ref traffic.Requests),
        Volatile.Read(ref traffic.Failed),
        Volatile.Read(ref traffic.Active),
        Volatile.Read(ref traffic.BytesIn),
        Volatile.Read(ref traffic.BytesOut),
        Average(traffic),
        Percentile(traffic.Buckets, Volatile.Read(ref traffic.Requests), 0.50),
        Percentile(traffic.Buckets, Volatile.Read(ref traffic.Requests), 0.95),
        Percentile(traffic.Buckets, Volatile.Read(ref traffic.Requests), 0.99),
        Volatile.Read(ref traffic.Class2xx),
        Volatile.Read(ref traffic.Class3xx),
        Volatile.Read(ref traffic.Class4xx),
        Volatile.Read(ref traffic.Class5xx),
        Volatile.Read(ref traffic.ClassOther),
        traffic.LastError,
        traffic.FirstSeen,
        traffic.LastSeen);

    private static double Average(HostTraffic traffic)
    {
        var count = Volatile.Read(ref traffic.Requests);
        return count == 0 ? 0 : Math.Round((double)Volatile.Read(ref traffic.DurationSum) / count, 1);
    }

    private static int BucketIndex(long durationMs)
    {
        for (var i = 0; i < BucketUpperMs.Length; i++)
        {
            if (durationMs < BucketUpperMs[i])
            {
                return i;
            }
        }

        return BucketUpperMs.Length - 1;
    }

    /// <summary>Approximates a percentile from the cumulative duration buckets (bucket midpoint).</summary>
    private static double Percentile(long[] buckets, long total, double percentile)
    {
        if (total <= 0)
        {
            return 0;
        }

        var target = (long)Math.Ceiling(percentile * total);
        long cumulative = 0;
        for (var i = 0; i < buckets.Length; i++)
        {
            cumulative += buckets[i];
            if (cumulative >= target)
            {
                var upper = BucketUpperMs[i];
                if (double.IsPositiveInfinity(upper))
                {
                    upper = BucketUpperMs[^2];
                }

                var lower = i == 0 ? 0 : BucketUpperMs[i - 1];
                return Math.Round((lower + upper) / 2.0, 1);
            }
        }

        return BucketUpperMs[^2];
    }

    private static string StatusClass(int statusCode) => statusCode switch
    {
        >= 200 and < 300 => "2xx",
        >= 300 and < 400 => "3xx",
        >= 400 and < 500 => "4xx",
        >= 500 => "5xx",
        _ => "other",
    };

    private static void IncrementClass(HostTraffic traffic, int statusCode)
    {
        switch (statusCode)
        {
            case >= 200 and < 300: Interlocked.Increment(ref traffic.Class2xx); break;
            case >= 300 and < 400: Interlocked.Increment(ref traffic.Class3xx); break;
            case >= 400 and < 500: Interlocked.Increment(ref traffic.Class4xx); break;
            case >= 500: Interlocked.Increment(ref traffic.Class5xx); break;
            default: Interlocked.Increment(ref traffic.ClassOther); break;
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private sealed class HostTraffic
    {
        internal long Requests;
        internal long Failed;
        internal long Active;
        internal long BytesIn;
        internal long BytesOut;
        internal long DurationSum;
        internal long Class2xx;
        internal long Class3xx;
        internal long Class4xx;
        internal long Class5xx;
        internal long ClassOther;
        internal readonly long[] Buckets = new long[BucketUpperMs.Length];
        internal string? LastError;
        internal DateTimeOffset FirstSeen;
        internal DateTimeOffset LastSeen;
    }
}
