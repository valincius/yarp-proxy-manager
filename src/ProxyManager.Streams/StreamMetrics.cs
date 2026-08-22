using System.Diagnostics.Metrics;

namespace ProxyManager.Streams;

/// <summary>
/// Prometheus-visible stream listener gauges on the <c>ProxyManager.Traffic</c> meter
/// (alongside the per-host HTTP traffic): active sessions, total bytes and listening
/// state per stream id. The diagnostics overview joins these with stream names.
/// </summary>
public sealed class StreamMetrics
{
    private readonly Meter _meter = new("ProxyManager.Traffic");
    private readonly StreamStatusRegistry _registry;

    public StreamMetrics(StreamStatusRegistry registry)
    {
        _registry = registry;
        _meter.CreateObservableGauge("traffic.stream.sessions",
            () => Snapshot().Select(s => new Measurement<long>(s.Value.ActiveSessions,
                new KeyValuePair<string, object?>("stream", s.Key.ToString()))),
            description: "Active TCP/UDP sessions per stream.");
        _meter.CreateObservableGauge("traffic.stream.bytes",
            () => Snapshot().Select(s => new Measurement<long>(s.Value.BytesIn + s.Value.BytesOut,
                new KeyValuePair<string, object?>("stream", s.Key.ToString()))),
            unit: "By",
            description: "Total bytes transferred per stream.");
        _meter.CreateObservableGauge("traffic.stream.listening",
            () => Snapshot().Select(s => new Measurement<long>(s.Value.Listening ? 1 : 0,
                new KeyValuePair<string, object?>("stream", s.Key.ToString()))),
            description: "Whether each stream listener is currently bound (1) or not (0).");
    }

    private IReadOnlyDictionary<Guid, StreamStatus> Snapshot() => _registry.Snapshot();
}
