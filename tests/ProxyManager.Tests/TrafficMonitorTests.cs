using FluentAssertions;
using ProxyManager.Proxy;
using Xunit;

namespace ProxyManager.Tests;

public sealed class TrafficMonitorTests
{
    private static RequestSample Sample(
        string host,
        int status,
        long durationMs,
        long bytesIn = 0,
        long bytesOut = 0,
        string? error = null,
        DateTimeOffset? timestamp = null) =>
        new(timestamp ?? DateTimeOffset.UtcNow, host, "GET", "/", status, durationMs,
            bytesIn, bytesOut, "10.0.0.1", error, null, null);

    [Fact]
    public void Snapshot_AggregatesCountsStatusClassesBytesAndLatency()
    {
        var monitor = new TrafficMonitor();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 6; i++)
        {
            monitor.Record(Sample("a.example.com", 200, 2, 100, 200, timestamp: now));
        }

        monitor.Record(Sample("a.example.com", 404, 30, 0, 100, timestamp: now));
        monitor.Record(Sample("a.example.com", 500, 5000, 0, 0, error: "boom", timestamp: now));

        var summary = monitor.Snapshot(null).Should().ContainSingle().Subject;
        summary.Host.Should().Be("a.example.com");
        summary.Requests.Should().Be(8);
        summary.Failed.Should().Be(1);
        summary.Class2xx.Should().Be(6);
        summary.Class3xx.Should().Be(0);
        summary.Class4xx.Should().Be(1);
        summary.Class5xx.Should().Be(1);
        summary.BytesIn.Should().Be(600);
        summary.BytesOut.Should().Be(1300);
        summary.LastError.Should().Be("boom");
        summary.AverageMs.Should().BeApproximately((2 * 6.0 + 30 + 5000) / 8, 0.5);
        summary.P50Ms.Should().BeGreaterThan(0);
        summary.P95Ms.Should().BeGreaterThan(summary.P50Ms);
        summary.FirstSeen.Should().Be(now);
        summary.LastSeen.Should().Be(now);
    }

    [Fact]
    public void Snapshot_WindowFiltersByTimestamp()
    {
        var monitor = new TrafficMonitor();
        var now = DateTimeOffset.UtcNow;
        monitor.Record(Sample("a.example.com", 200, 1, timestamp: now));
        monitor.Record(Sample("a.example.com", 200, 1, timestamp: now.AddMinutes(-2)));
        monitor.Record(Sample("a.example.com", 200, 1, timestamp: now.AddMinutes(-10)));

        monitor.Snapshot(TimeSpan.FromMinutes(1)).Single().Requests.Should().Be(1);
        monitor.Snapshot(TimeSpan.FromMinutes(5)).Single().Requests.Should().Be(2);
        monitor.Snapshot(null).Single().Requests.Should().Be(3);
    }

    [Fact]
    public void Snapshot_GroupsByHostnameCaseInsensitively()
    {
        var monitor = new TrafficMonitor();
        monitor.Record(Sample("App.Example.COM", 200, 1));
        monitor.Record(Sample("app.example.com", 200, 1));

        monitor.Snapshot(null).Should().ContainSingle().Subject.Requests.Should().Be(2);
    }

    [Fact]
    public void RecentRequests_ReturnsNewestFirstRespectingLimit()
    {
        var monitor = new TrafficMonitor();
        var now = DateTimeOffset.UtcNow;
        for (var i = 1; i <= 5; i++)
        {
            monitor.Record(Sample($"h{i}.test", 200, 1, timestamp: now.AddSeconds(i)));
        }

        var recent = monitor.RecentRequests(3);
        recent.Should().HaveCount(3);
        recent[0].Host.Should().Be("h5.test");
        recent[^1].Host.Should().Be("h3.test");
    }

    [Fact]
    public void Overview_TracksTotalsAndHostCount()
    {
        var monitor = new TrafficMonitor();
        monitor.Record(Sample("a.test", 200, 1));
        monitor.Record(Sample("a.test", 500, 1, error: "x"));
        monitor.Record(Sample("b.test", 404, 1));

        var overview = monitor.Overview();
        overview.TotalRequests.Should().Be(3);
        overview.TotalFailed.Should().Be(1);
        overview.TrackedHosts.Should().Be(2);
        overview.BufferedSamples.Should().Be(3);
    }

    [Fact]
    public void StartEndRequest_TracksActiveCount()
    {
        var monitor = new TrafficMonitor();
        monitor.StartRequest("a.test");
        monitor.StartRequest("a.test");
        monitor.EndRequest("a.test");

        monitor.Snapshot(null).Single().Active.Should().Be(1);
    }
}
