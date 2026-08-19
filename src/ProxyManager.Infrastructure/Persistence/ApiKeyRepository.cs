using Microsoft.EntityFrameworkCore;
using ProxyManager.Application.ApiKeys;
using ProxyManager.Domain;

namespace ProxyManager.Infrastructure.Persistence;

public sealed class ApiKeyRepository(ProxyDbContext db) : IApiKeyRepository
{
    public async Task<IReadOnlyList<ApiKey>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.ApiKeys.AsNoTracking().OrderBy(k => k.Name).ToListAsync(cancellationToken);

    public async Task<ApiKey?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Id == id, cancellationToken);

    public async Task<ApiKey?> GetByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        await db.ApiKeys.FirstOrDefaultAsync(k => k.Prefix == prefix, cancellationToken);

    public async Task AddAsync(ApiKey apiKey, CancellationToken cancellationToken = default)
    {
        db.ApiKeys.Add(apiKey);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(ApiKey apiKey, CancellationToken cancellationToken = default)
    {
        db.ApiKeys.Remove(apiKey);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task TouchAsync(ApiKey apiKey, CancellationToken cancellationToken = default)
    {
        db.ApiKeys.Update(apiKey);
        await db.SaveChangesAsync(cancellationToken);
    }
}
