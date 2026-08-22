namespace ProxyManager.Certificates.Acme;

/// <summary>One pending challenge of an ACME order.</summary>
public sealed record AcmeChallengeDescriptor(
    string Token,
    string Type,          // "http-01" | "dns-01"
    string Domain,        // the identifier the authorization covers (wildcard label may be present)
    string KeyAuthorization,
    string? DnsRecordName,   // "_acme-challenge.{domain}" without a wildcard label — dns-01 only
    string? DnsRecordValue); // TXT value — dns-01 only

public sealed record AcmeIssuedCertificate(byte[] Pfx, DateTimeOffset NotBefore, DateTimeOffset NotAfter);

/// <summary>
/// ACME client abstraction over Certes. One instance drives one issuance session
/// (initialize → order → challenges → validate → finalize).
/// </summary>
public interface IAcmeClient : IDisposable
{
    Task InitializeAsync(string email, string directoryUrl, string accountKeyPem, CancellationToken cancellationToken);

    Task<string> CreateOrderAsync(string[] domains, CancellationToken cancellationToken);

    Task<IReadOnlyList<AcmeChallengeDescriptor>> GetPendingChallengesAsync(string orderId, CancellationToken cancellationToken);

    Task ValidateChallengeAsync(string orderId, string token, CancellationToken cancellationToken);

    /// <summary>
    /// Polls public DNS until the TXT record for a DNS-01 challenge is visible with the
    /// expected value (or the timeout elapses). The CA rejects challenges that are
    /// validated before the record propagates, so this must run before ValidateChallengeAsync.
    /// </summary>
    Task WaitForTxtPropagationAsync(string recordName, string expectedValue, TimeSpan timeout, CancellationToken cancellationToken);

    Task WaitForChallengeAsync(string orderId, string token, TimeSpan timeout, CancellationToken cancellationToken);

    Task<AcmeIssuedCertificate> FinalizeAsync(
        string orderId,
        string commonName,
        string[] sanDomains,
        string pfxPassword,
        CancellationToken cancellationToken);
}
