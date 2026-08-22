using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProxyManager.Application.Certificates;
using ProxyManager.Application.ProxyHosts;
using ProxyManager.Application.Settings;
using ProxyManager.Domain;
using ProxyManager.Proxy;
using ProxyManager.Streams;
using Yarp.ReverseProxy.Configuration;

namespace ProxyManager.Api.Controllers;

/// <summary>
/// Live traffic statistics and system diagnostics — the observability surface for
/// validating the proxy before/while replacing NPM. Traffic summaries and the overview
/// are available to any authenticated principal (cookie session or API key); the
/// recent-requests view (which may contain captured bodies) is admin-only.
/// </summary>
[Route("api/v1/diagnostics")]
public sealed class DiagnosticsController(
    TrafficMonitor monitor,
    IProxyHostRepository hosts,
    ICertificateRepository certificates,
    StreamStatusRegistry streams,
    IProxyConfigProvider proxyConfig,
    SettingsService settings,
    IConfiguration configuration) : ApiControllerBase
{
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken cancellationToken)
    {
        var overview = monitor.Overview();
        var config = proxyConfig.GetConfig();
        var hostList = await hosts.ListAsync(cancellationToken);
        var certList = await certificates.ListCertificatesAsync(cancellationToken);
        var diagnosticsSettings = await settings.GetDiagnosticsSettingsAsync(cancellationToken);

        return Ok(new
        {
            overview.StartedAt,
            overview.TotalRequests,
            overview.TotalFailed,
            overview.TrackedHosts,
            overview.BufferedSamples,
            captureEnabled = diagnosticsSettings.CaptureEnabled,
            captureSize = diagnosticsSettings.CaptureSize,
            traceEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
                ?? configuration["Diagnostics:Tracing:Endpoint"],
            routes = config.Routes.Count,
            clusters = config.Clusters.Count,
            proxyHosts = hostList.Count,
            streams = streams.Snapshot().Select(s => new
            {
                streamId = s.Key,
                s.Value.Listening,
                s.Value.ActiveSessions,
                s.Value.BytesIn,
                s.Value.BytesOut,
                s.Value.Error,
                s.Value.UpdatedAt,
            }).ToList(),
            certificates = new
            {
                total = certList.Count,
                failed = certList.Count(c => c.Status == CertificateStatus.Failed),
                expiringSoon = certList.Count(c =>
                    c.Status == CertificateStatus.Issued
                    && c.NotAfter is { } notAfter
                    && notAfter < DateTimeOffset.UtcNow.AddDays(30)),
            },
        });
    }

    /// <summary>Per-hostname traffic. Window: <c>all</c> (default), <c>1m</c>, <c>5m</c> or <c>15m</c>.</summary>
    [HttpGet("traffic")]
    public async Task<IActionResult> Traffic([FromQuery] string? window, CancellationToken cancellationToken)
    {
        TimeSpan? span = window switch
        {
            null or "" or "all" => null,
            "1m" => TimeSpan.FromMinutes(1),
            "5m" => TimeSpan.FromMinutes(5),
            "15m" => TimeSpan.FromMinutes(15),
            _ => null,
        };

        var rows = monitor.Snapshot(span);
        var hostIndex = BuildHostIndex(await hosts.ListAsync(cancellationToken));

        return Ok(rows.Select(r => new
        {
            r.Host,
            hostId = ResolveHostId(r.Host, hostIndex)?.Id,
            hostName = ResolveHostId(r.Host, hostIndex)?.Name,
            r.Requests,
            r.Failed,
            r.Active,
            r.BytesIn,
            r.BytesOut,
            r.AverageMs,
            r.P50Ms,
            r.P95Ms,
            r.P99Ms,
            r.Class2xx,
            r.Class3xx,
            r.Class4xx,
            r.Class5xx,
            r.ClassOther,
            r.LastError,
            r.FirstSeen,
            r.LastSeen,
        }));
    }

    /// <summary>Most recent proxied requests, newest first. May include captured bodies (admin only).</summary>
    [HttpGet("requests")]
    [Authorize(Roles = "Admin")]
    public IActionResult Requests([FromQuery] int limit = 100)
        => Ok(monitor.RecentRequests(Math.Clamp(limit, 1, 1000)));

    private static Dictionary<string, (Guid Id, string Name)> BuildHostIndex(IReadOnlyList<ProxyHost> hostList)
    {
        var index = new Dictionary<string, (Guid, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in hostList)
        {
            foreach (var domain in host.DomainNames)
            {
                index.TryAdd(domain.Trim().ToLowerInvariant(), (host.Id, host.Name));
            }
        }

        return index;
    }

    private static (Guid Id, string Name)? ResolveHostId(string host, Dictionary<string, (Guid Id, string Name)> index)
    {
        if (index.TryGetValue(host, out var exact))
        {
            return exact;
        }

        var labels = host.Split('.');
        for (var i = 1; i < labels.Length - 1; i++)
        {
            var wildcard = "*." + string.Join('.', labels[i..]);
            if (index.TryGetValue(wildcard, out var wild))
            {
                return wild;
            }
        }

        return null;
    }
}
