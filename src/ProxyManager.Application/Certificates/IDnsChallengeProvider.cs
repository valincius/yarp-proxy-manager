namespace ProxyManager.Application.Certificates;

/// <summary>Adds/removes TXT records for DNS-01 challenges on the zone that owns a domain.</summary>
public interface IDnsChallengeProvider
{
    /// <param name="domain">The certificate domain being validated (wildcard label may be present).</param>
    /// <param name="recordName">Full record name, e.g. "_acme-challenge.example.com".</param>
    /// <param name="recordValue">The TXT value (the key authorization hash).</param>
    Task AddTxtRecordAsync(string domain, string recordName, string recordValue, CancellationToken cancellationToken = default);

    Task RemoveTxtRecordAsync(string domain, string recordName, string recordValue, CancellationToken cancellationToken = default);
}
