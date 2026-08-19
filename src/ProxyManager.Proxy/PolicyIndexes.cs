using ProxyManager.Application.Proxy;

namespace ProxyManager.Proxy;

/// <summary>Hostname → protection policy (access list, exploit blocking, ForceHTTPS). Updated by the reloader.</summary>
public sealed class HostPolicyIndex
{
    private volatile IReadOnlyDictionary<string, HostPolicy> _policies =
        new Dictionary<string, HostPolicy>(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string host, out HostPolicy policy) => _policies.TryGetValue(host, out policy!);

    public void Update(IReadOnlyDictionary<string, HostPolicy> policies) => _policies = policies;
}

/// <summary>Hostname → redirect configuration. Updated by the reloader.</summary>
public sealed class RedirectIndex
{
    private volatile IReadOnlyDictionary<string, RedirectConfig> _redirects =
        new Dictionary<string, RedirectConfig>(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string host, out RedirectConfig redirect) => _redirects.TryGetValue(host, out redirect!);

    /// <summary>Matches exact hostnames first, then wildcard entries for parent domains.</summary>
    public bool TryMatch(string host, out RedirectConfig redirect)
    {
        if (_redirects.TryGetValue(host, out redirect!))
        {
            return true;
        }

        var labels = host.Split('.');
        for (var i = 1; i < labels.Length - 1; i++)
        {
            var wildcard = "*." + string.Join('.', labels[i..]);
            if (_redirects.TryGetValue(wildcard, out redirect!))
            {
                return true;
            }
        }

        redirect = null!;
        return false;
    }

    public void Update(IEnumerable<RedirectConfig> redirects)
    {
        var next = new Dictionary<string, RedirectConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var redirect in redirects)
        {
            foreach (var domain in redirect.Domains)
            {
                next[domain] = redirect;
            }
        }

        _redirects = next;
    }
}
