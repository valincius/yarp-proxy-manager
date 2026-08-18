using ProxyManager.Application.Certificates;

namespace ProxyManager.Infrastructure.Dns;

/// <summary>Creates a DNS challenge provider for the requested provider key + API token.</summary>
public sealed class DnsChallengeProviderFactory(IHttpClientFactory httpClientFactory) : IDnsChallengeProviderFactory
{
    public IDnsChallengeProvider Create(string provider, string apiToken) =>
        provider.ToLowerInvariant() switch
        {
            "cloudflare" => new CloudflareDnsChallengeProvider(httpClientFactory, apiToken),
            _ => throw new InvalidOperationException($"Unsupported DNS provider '{provider}'."),
        };
}
