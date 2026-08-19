using Microsoft.EntityFrameworkCore;
using ProxyManager.Application.Proxy;
using ProxyManager.Domain;

namespace ProxyManager.Infrastructure.Persistence;

public sealed class AccessListStore(ProxyDbContext db) : IAccessListStore
{
    public async Task<IReadOnlyDictionary<Guid, AccessListPolicy>> GetAccessListsAsync(CancellationToken cancellationToken = default)
    {
        var lists = await db.AccessLists.AsNoTracking()
            .Include(a => a.Rules)
            .ToListAsync(cancellationToken);

        return lists.ToDictionary(
            a => a.Id,
            a => new AccessListPolicy(
                a.SatisfyAny,
                a.Rules.Select(r => new AccessRule(r.Action, r.Pattern)).ToList()));
    }
}

public sealed class RedirectStore(ProxyDbContext db) : IRedirectStore
{
    public async Task<IReadOnlyList<RedirectConfig>> GetEnabledRedirectsAsync(CancellationToken cancellationToken = default)
    {
        var redirects = await db.RedirectHosts.AsNoTracking()
            .Where(r => r.Enabled)
            .ToListAsync(cancellationToken);

        return redirects
            .Select(r => new RedirectConfig(
                r.DomainNames.ToArray(),
                r.ForwardScheme,
                r.ForwardHost,
                r.ForwardPort,
                r.StatusCode,
                r.PreservePath))
            .ToList();
    }
}
