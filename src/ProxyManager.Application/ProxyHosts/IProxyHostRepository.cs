using ProxyManager.Domain;

namespace ProxyManager.Application.ProxyHosts;

public interface IProxyHostRepository
{
    Task<IReadOnlyList<ProxyHost>> ListAsync(CancellationToken cancellationToken = default);

    Task<ProxyHost?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Finds a managed host by its source identifier (e.g. "container:&lt;id&gt;").</summary>
    Task<ProxyHost?> FindByManagedSourceAsync(string source, CancellationToken cancellationToken = default);

    /// <summary>Lists hosts owned by an automated source ("docker").</summary>
    Task<IReadOnlyList<ProxyHost>> ListManagedAsync(string managedBy, CancellationToken cancellationToken = default);

    Task AddAsync(ProxyHost host, CancellationToken cancellationToken = default);

    Task UpdateAsync(ProxyHost host, CancellationToken cancellationToken = default);

    Task DeleteAsync(ProxyHost host, CancellationToken cancellationToken = default);
}
