namespace ProxyManager.Proxy;

/// <summary>
/// The set of hostnames that must be redirected to HTTPS (hosts with ForceHttps enabled
/// and a valid certificate). Updated by <see cref="ProxyConfigReloader"/>.
/// </summary>
public sealed class ForceHttpsIndex
{
    private volatile HashSet<string> _domains = new(StringComparer.OrdinalIgnoreCase);

    public bool Contains(string host) => _domains.Contains(host);

    public void Update(IEnumerable<string> domains) =>
        _domains = new HashSet<string>(domains, StringComparer.OrdinalIgnoreCase);
}
