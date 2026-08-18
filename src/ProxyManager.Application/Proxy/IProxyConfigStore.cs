using ProxyManager.Application.ProxyHosts;

namespace ProxyManager.Application.Proxy;

/// <summary>
/// Loads the enabled proxy hosts from the database in the shape the proxy pipeline needs.
/// Implemented in ProxyManager.Infrastructure.
/// </summary>
public interface IProxyConfigStore
{
    Task<IReadOnlyList<HostConfig>> GetEnabledHostsAsync(CancellationToken cancellationToken = default);
}
