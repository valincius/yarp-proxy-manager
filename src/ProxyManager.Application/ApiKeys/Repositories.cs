using ProxyManager.Domain;

namespace ProxyManager.Application.ApiKeys;

public interface IApiKeyRepository
{
    Task<IReadOnlyList<ApiKey>> ListAsync(CancellationToken cancellationToken = default);

    Task<ApiKey?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ApiKey?> GetByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    Task AddAsync(ApiKey apiKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(ApiKey apiKey, CancellationToken cancellationToken = default);

    /// <summary>Persists usage metadata (LastUsedAt) for a key already loaded from the store.</summary>
    Task TouchAsync(ApiKey apiKey, CancellationToken cancellationToken = default);
}
