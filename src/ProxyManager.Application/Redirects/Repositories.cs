using ProxyManager.Domain;

namespace ProxyManager.Application.Redirects;

public interface IRedirectHostRepository
{
    Task<IReadOnlyList<RedirectHost>> ListAsync(CancellationToken cancellationToken = default);

    Task<RedirectHost?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(RedirectHost redirect, CancellationToken cancellationToken = default);

    Task UpdateAsync(RedirectHost redirect, CancellationToken cancellationToken = default);

    Task DeleteAsync(RedirectHost redirect, CancellationToken cancellationToken = default);
}

public interface IAccessListRepository
{
    Task<IReadOnlyList<AccessList>> ListAsync(CancellationToken cancellationToken = default);

    Task<AccessList?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(AccessList accessList, CancellationToken cancellationToken = default);

    Task UpdateAsync(AccessList accessList, CancellationToken cancellationToken = default);

    Task DeleteAsync(AccessList accessList, CancellationToken cancellationToken = default);
}

public interface IAuditLogRepository
{
    Task<IReadOnlyList<AuditLog>> ListAsync(int limit, string? entityType, CancellationToken cancellationToken = default);
}
