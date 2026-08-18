using ProxyManager.Application.Certificates;

namespace ProxyManager.Application.Certificates;

/// <summary>Creates an <see cref="IDnsChallengeProvider"/> for a provider key + decrypted API token.</summary>
public interface IDnsChallengeProviderFactory
{
    IDnsChallengeProvider Create(string provider, string apiToken);
}
