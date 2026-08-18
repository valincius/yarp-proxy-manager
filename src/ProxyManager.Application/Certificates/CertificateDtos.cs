namespace ProxyManager.Application.Certificates;

public sealed record IssueCertificateRequest(
    string Name,
    IReadOnlyList<string> Domains,
    string ChallengeType,
    Guid? DnsCredentialId);

public sealed record UploadCertificateRequest(
    string Name,
    IReadOnlyList<string> Domains,
    string? PfxBase64,
    string? PfxPassword,
    string? CertificatePem,
    string? PrivateKeyPem);

public sealed record DnsCredentialInput(string Name, string ApiToken);

public sealed record AcmeSettingsDto(string Email, string DirectoryUrl, bool Staging);
