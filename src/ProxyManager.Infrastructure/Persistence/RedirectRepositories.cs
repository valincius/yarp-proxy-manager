using Microsoft.EntityFrameworkCore;
using ProxyManager.Application.Redirects;
using ProxyManager.Domain;

namespace ProxyManager.Infrastructure.Persistence;

public sealed class RedirectHostRepository(ProxyDbContext db) : IRedirectHostRepository
{
    public async Task<IReadOnlyList<RedirectHost>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.RedirectHosts.AsNoTracking().OrderBy(r => r.Name).ToListAsync(cancellationToken);

    public async Task<RedirectHost?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.RedirectHosts.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task AddAsync(RedirectHost redirect, CancellationToken cancellationToken = default)
    {
        db.RedirectHosts.Add(redirect);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(RedirectHost redirect, CancellationToken cancellationToken = default)
    {
        db.RedirectHosts.Update(redirect);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(RedirectHost redirect, CancellationToken cancellationToken = default)
    {
        db.RedirectHosts.Remove(redirect);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class AccessListRepository(ProxyDbContext db) : IAccessListRepository
{
    public async Task<IReadOnlyList<AccessList>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.AccessLists.AsNoTracking()
            .Include(a => a.Rules)
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);

    public async Task<AccessList?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.AccessLists.AsNoTracking()
            .Include(a => a.Rules)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task AddAsync(AccessList accessList, CancellationToken cancellationToken = default)
    {
        db.AccessLists.Add(accessList);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Replaces the rules wholesale (EF cannot diff replaced child collections).</summary>
    public async Task UpdateAsync(AccessList accessList, CancellationToken cancellationToken = default)
    {
        var tracked = await db.AccessLists.Include(a => a.Rules)
            .FirstAsync(a => a.Id == accessList.Id, cancellationToken);

        db.AccessListRules.RemoveRange(tracked.Rules);
        db.Entry(tracked).CurrentValues.SetValues(accessList);
        tracked.Rules = accessList.Rules;
        db.AccessListRules.AddRange(tracked.Rules);

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(AccessList accessList, CancellationToken cancellationToken = default)
    {
        db.AccessLists.Remove(accessList);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class AuditLogRepository(ProxyDbContext db) : IAuditLogRepository
{
    public async Task<IReadOnlyList<AuditLog>> ListAsync(int limit, string? entityType, CancellationToken cancellationToken = default)
    {
        var query = db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(a => a.EntityType == entityType);
        }

        // SQLite cannot ORDER BY DateTimeOffset — order on the client side.
        return (await query.ToListAsync(cancellationToken))
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .ToList();
    }
}
