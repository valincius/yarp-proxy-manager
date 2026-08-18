using Microsoft.EntityFrameworkCore;
using ProxyManager.Application.ProxyHosts;
using ProxyManager.Domain;

namespace ProxyManager.Infrastructure.Persistence;

public sealed class ProxyHostRepository(ProxyDbContext db) : IProxyHostRepository
{
    private static IQueryable<ProxyHost> WithChildren(IQueryable<ProxyHost> query) =>
        query
            .Include(h => h.Locations)
            .Include(h => h.RequestHeaders)
            .Include(h => h.ResponseHeaders);

    public async Task<IReadOnlyList<ProxyHost>> ListAsync(CancellationToken cancellationToken = default) =>
        await WithChildren(db.ProxyHosts.AsNoTracking())
            .OrderBy(h => h.Name)
            .ToListAsync(cancellationToken);

    public async Task<ProxyHost?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await WithChildren(db.ProxyHosts.AsNoTracking())
            .FirstOrDefaultAsync(h => h.Id == id, cancellationToken);

    public async Task AddAsync(ProxyHost host, CancellationToken cancellationToken = default)
    {
        db.ProxyHosts.Add(host);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Replaces the child collections (locations/headers) wholesale: EF Core cannot diff
    /// replaced collections, so tracked children are removed and the new ones added.
    /// </summary>
    public async Task UpdateAsync(ProxyHost host, CancellationToken cancellationToken = default)
    {
        var tracked = await WithChildren(db.ProxyHosts)
            .FirstAsync(h => h.Id == host.Id, cancellationToken);

        db.ProxyLocations.RemoveRange(tracked.Locations);
        db.ProxyHeaders.RemoveRange(tracked.RequestHeaders);
        db.ProxyHeaders.RemoveRange(tracked.ResponseHeaders);

        db.Entry(tracked).CurrentValues.SetValues(host);
        tracked.Locations = host.Locations;
        tracked.RequestHeaders = host.RequestHeaders;
        tracked.ResponseHeaders = host.ResponseHeaders;

        db.ProxyLocations.AddRange(tracked.Locations);
        db.ProxyHeaders.AddRange(tracked.RequestHeaders.Concat(tracked.ResponseHeaders));

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(ProxyHost host, CancellationToken cancellationToken = default)
    {
        db.ProxyHosts.Remove(host);
        await db.SaveChangesAsync(cancellationToken);
    }
}
