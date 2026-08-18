using System.Net.Http.Json;
using System.Text.Json;
using ProxyManager.Application.Certificates;

namespace ProxyManager.Infrastructure.Dns;

/// <summary>DNS-01 challenge provider backed by the Cloudflare API (api.cloudflare.com).</summary>
public sealed class CloudflareDnsChallengeProvider(
    IHttpClientFactory httpClientFactory,
    string apiToken) : IDnsChallengeProvider
{
    private const string BaseUrl = "https://api.cloudflare.com/client/v4";

    public async Task AddTxtRecordAsync(
        string domain,
        string recordName,
        string recordValue,
        CancellationToken cancellationToken = default)
    {
        var zoneId = await FindZoneAsync(domain, cancellationToken);
        var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            $"{BaseUrl}/zones/{zoneId}/dns_records",
            new { type = "TXT", name = recordName, content = recordValue, ttl = 120 },
            cancellationToken);
        await EnsureSuccessAsync(response, "creating TXT record", cancellationToken);
    }

    public async Task RemoveTxtRecordAsync(
        string domain,
        string recordName,
        string recordValue,
        CancellationToken cancellationToken = default)
    {
        var zoneId = await FindZoneAsync(domain, cancellationToken);
        var client = CreateClient();

        var url = $"{BaseUrl}/zones/{zoneId}/dns_records?type=TXT&name={Uri.EscapeDataString(recordName)}&content={Uri.EscapeDataString(recordValue)}";
        var lookup = await client.GetFromJsonAsync<CloudflareResponse<DnsRecord>>(url, cancellationToken);
        if (lookup is null || !lookup.Success || lookup.Result is null || lookup.Result.Length == 0)
        {
            return; // already gone
        }

        foreach (var record in lookup.Result)
        {
            var response = await client.DeleteAsync($"{BaseUrl}/zones/{zoneId}/dns_records/{record.Id}", cancellationToken);
            await EnsureSuccessAsync(response, "deleting TXT record", cancellationToken);
        }
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient("CloudflareDns");
        client.DefaultRequestHeaders.Authorization = new("Bearer", apiToken);
        return client;
    }

    private async Task<string> FindZoneAsync(string domain, CancellationToken cancellationToken)
    {
        // Walk the domain suffixes from longest to shortest; the first suffix that is a
        // Cloudflare zone wins (e.g. for "a.b.example.com": a.b.example.com, b.example.com, example.com).
        var suffixes = EnumerateSuffixes(domain.StartsWith("*.", StringComparison.Ordinal) ? domain[2..] : domain);
        foreach (var suffix in suffixes)
        {
            var client = CreateClient();
            var url = $"{BaseUrl}/zones?name={Uri.EscapeDataString(suffix)}&per_page=1";
            var response = await client.GetFromJsonAsync<CloudflareResponse<Zone>>(url, cancellationToken);
            if (response is not null && response.Success && response.Result is { Length: > 0 })
            {
                return response.Result[0].Id;
            }
        }

        throw new InvalidOperationException(
            $"No Cloudflare zone was found for domain '{domain}'. Ensure the domain is a zone (or subdomain of a zone) in the Cloudflare account.");
    }

    private static IEnumerable<string> EnumerateSuffixes(string domain)
    {
        var labels = domain.Split('.');
        for (var i = 0; i < labels.Length; i++)
        {
            yield return string.Join('.', labels[i..]);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Cloudflare API failed while {operation} ({(int)response.StatusCode}): {body}");
    }

    private sealed record CloudflareResponse<T>(bool Success, T[]? Result, object[]? Errors);

    private sealed record Zone(string Id, string Name);

    private sealed record DnsRecord(string Id, string Type, string Name, string Content);
}
