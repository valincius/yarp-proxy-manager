namespace ProxyManager.Domain;

public enum CertificateProvider
{
    Manual,
    Acme,
}

public enum CertificateStatus
{
    Pending,
    Issued,
    Failed,
    Revoked,
}

/// <summary>A TLS certificate for one or more domains, either uploaded manually or issued via ACME.</summary>
public sealed class Certificate
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<string> Domains { get; set; } = [];

    public CertificateProvider Provider { get; set; }

    public CertificateStatus Status { get; set; }

    public DateTimeOffset? NotBefore { get; set; }

    public DateTimeOffset? NotAfter { get; set; }

    /// <summary>Path to the PFX file on disk (relative to the certificates directory).</summary>
    public string? PfxPath { get; set; }

    /// <summary>Data-Protection-protected PFX password.</summary>
    public string? EncryptedPfxPassword { get; set; }

    /// <summary>"Http01" or "Dns01" — ACME only.</summary>
    public string? ChallengeType { get; set; }

    /// <summary>DNS credential used for DNS-01 challenges (ACME only).</summary>
    public Guid? DnsCredentialId { get; set; }

    public DateTimeOffset? LastRenewalAttempt { get; set; }

    public string? LastRenewalError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Stored credentials for a DNS provider used to satisfy DNS-01 challenges.</summary>
public sealed class DnsCredential
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Provider key, e.g. "Cloudflare".</summary>
    public string Provider { get; set; } = "Cloudflare";

    /// <summary>Data-Protection-protected API token.</summary>
    public string EncryptedApiToken { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>The single ACME account used for certificate issuance (email + CA directory + account key).</summary>
public sealed class AcmeAccount
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    /// <summary>Data-Protection-protected account key (PKCS#8 PEM).</summary>
    public string EncryptedAccountKey { get; set; } = string.Empty;

    /// <summary>ACME directory URL (Let's Encrypt production/staging or a custom CA).</summary>
    public string DirectoryUrl { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
