namespace ProxyManager.Application.Proxy;

/// <summary>One access-list rule projected for the proxy pipeline.</summary>
public sealed record AccessRule(string Action, string Pattern);

public sealed record AccessListPolicy(bool SatisfyAny, IReadOnlyList<AccessRule> Rules);

/// <summary>Per-host protection policy used by the proxy middleware.</summary>
public sealed record HostPolicy(bool BlockExploits, bool ForceHttps, AccessListPolicy? AccessList);

/// <summary>An enabled redirect host projected for the proxy pipeline.</summary>
public sealed record RedirectConfig(
    string[] Domains,
    string Scheme,
    string Host,
    int Port,
    int StatusCode,
    bool PreservePath);

/// <summary>Loads access lists for the proxy pipeline (Infrastructure).</summary>
public interface IAccessListStore
{
    Task<IReadOnlyDictionary<Guid, AccessListPolicy>> GetAccessListsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Loads enabled redirect hosts for the proxy pipeline (Infrastructure).</summary>
public interface IRedirectStore
{
    Task<IReadOnlyList<RedirectConfig>> GetEnabledRedirectsAsync(CancellationToken cancellationToken = default);
}
