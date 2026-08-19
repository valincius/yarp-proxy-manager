namespace ProxyManager.Domain;

/// <summary>A hostname that 301/302-redirects to another destination (NPM-style redirection host).</summary>
public sealed class RedirectHost
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<string> DomainNames { get; set; } = [];

    public bool Enabled { get; set; } = true;

    /// <summary>301 or 302.</summary>
    public int StatusCode { get; set; } = 301;

    /// <summary>When true, the request path and query string are appended to the redirect target.</summary>
    public bool PreservePath { get; set; } = true;

    public string ForwardScheme { get; set; } = "http";

    public string ForwardHost { get; set; } = string.Empty;

    public int ForwardPort { get; set; } = 80;

    public Guid? CertificateId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>An access list of allow/deny rules attached to proxy hosts.</summary>
public sealed class AccessList
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>NPM semantics: true = "satisfy any" (allow if any rule matches), false = "satisfy all".</summary>
    public bool SatisfyAny { get; set; } = true;

    public List<AccessListRule> Rules { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AccessListRule
{
    public Guid Id { get; set; }

    public Guid AccessListId { get; set; }

    /// <summary>"Allow" or "Deny".</summary>
    public string Action { get; set; } = "Allow";

    /// <summary>An IP address, CIDR block, or "*".</summary>
    public string Pattern { get; set; } = "*";
}

/// <summary>Immutable audit record of configuration changes.</summary>
public sealed class AuditLog
{
    public Guid Id { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public Guid? UserId { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public Guid? EntityId { get; set; }

    public string Action { get; set; } = string.Empty;

    /// <summary>JSON snapshot of the change (property → old/new).</summary>
    public string Details { get; set; } = "{}";
}
