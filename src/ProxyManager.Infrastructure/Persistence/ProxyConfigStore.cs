using Microsoft.EntityFrameworkCore;
using ProxyManager.Application.Proxy;
using ProxyManager.Application.ProxyHosts;
using ProxyManager.Domain;

namespace ProxyManager.Infrastructure.Persistence;

public sealed class ProxyConfigStore(ProxyDbContext db) : IProxyConfigStore
{
    public async Task<IReadOnlyList<HostConfig>> GetEnabledHostsAsync(CancellationToken cancellationToken = default)
    {
        var hosts = await db.ProxyHosts
            .AsNoTracking()
            .Where(h => h.Enabled)
            .Include(h => h.Locations)
            .Include(h => h.RequestHeaders)
            .Include(h => h.ResponseHeaders)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var certificates = (await db.Certificates.AsNoTracking().ToListAsync(cancellationToken))
            .Where(c => c.Status == CertificateStatus.Issued && c.NotAfter is { } notAfter && notAfter > now)
            .ToDictionary(c => c.Id);

        return hosts
            .Select(h => new HostConfig(
                h.Id,
                h.DomainNames.ToArray(),
                h.Scheme,
                h.ForwardHost,
                h.ForwardPort,
                h.BlockCommonExploits,
                h.ForceHttps,
                h.CertificateId is { } certificateId && certificates.ContainsKey(certificateId),
                h.AccessListId,
                h.Destinations.OrderBy(d => d.ForwardHost).Select(ToDestination).ToList(),
                h.LoadBalancingPolicy,
                h.HealthCheckEnabled,
                h.HealthCheckPath,
                h.HealthCheckIntervalSeconds,
                h.Locations.OrderBy(l => l.Order).Select(ToLocation).ToList(),
                h.RequestHeaders.Select(ToHeader).ToList(),
                h.ResponseHeaders.Select(ToHeader).ToList()))
            .ToList();
    }

    private static DestinationConfig ToDestination(ProxyDestination d) =>
        new(d.ForwardHost, d.ForwardPort);

    private static LocationConfig ToLocation(ProxyLocation l) =>
        new(l.PathPrefix, l.StripPrefix, l.Scheme, l.ForwardHost, l.ForwardPort);

    private static HeaderConfig ToHeader(ProxyHeader h) =>
        new(h.Target, h.Action, h.Name, h.Value);
}
