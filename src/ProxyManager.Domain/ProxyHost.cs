namespace ProxyManager.Domain;

/// <summary>
/// A reverse-proxy host entry, the primary domain object of the manager.
/// Maps 1:1 to a YARP route + cluster at runtime (see ProxyManager.Proxy).
/// </summary>
public sealed class ProxyHost
{
    public Guid Id { get; set; }

    /// <summary>Display name shown in the admin UI.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Hostnames the proxy accepts for this host (e.g. "app.example.com", "*.example.com").</summary>
    public List<string> DomainNames { get; set; } = [];

    public bool Enabled { get; set; } = true;

    /// <summary>"http" or "https" — scheme used to reach the upstream destination.</summary>
    public string Scheme { get; set; } = "http";

    public string ForwardHost { get; set; } = string.Empty;

    public int ForwardPort { get; set; } = 80;

    /// <summary>UI parity flag. YARP handles WebSocket upgrades natively, so this is stored but not
    /// otherwise consulted by the proxy pipeline.</summary>
    public bool WebSocketsEnabled { get; set; } = true;

    /// <summary>When enabled, requests matching common exploit patterns are rejected (Phase 3 middleware).</summary>
    public bool BlockCommonExploits { get; set; } = true;

    /// <summary>When set (requires a certificate), HTTP requests are 301-redirected to HTTPS.</summary>
    public bool ForceHttps { get; set; }

    public bool Http2Support { get; set; } = true;

    public Guid? CertificateId { get; set; }

    public Guid? AccessListId { get; set; }

    public List<ProxyHeader> RequestHeaders { get; set; } = [];

    public List<ProxyHeader> ResponseHeaders { get; set; } = [];

    /// <summary>Optional custom locations: path prefixes routed to their own upstream.</summary>
    public List<ProxyLocation> Locations { get; set; } = [];

    /// <summary>
    /// Optional multiple destinations for load balancing. When empty, the single
    /// ForwardHost/ForwardPort above is used.
    /// </summary>
    public List<ProxyDestination> Destinations { get; set; } = [];

    /// <summary>YARP load-balancing policy ("roundrobin", "leastrequests", "random", "poweroftwochoices" or "first").</summary>
    public string? LoadBalancingPolicy { get; set; }

    public bool HealthCheckEnabled { get; set; }

    public string? HealthCheckPath { get; set; }

    public int HealthCheckIntervalSeconds { get; set; } = 10;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>An upstream destination for load-balanced hosts.</summary>
public sealed class ProxyDestination
{
    public Guid Id { get; set; }

    public Guid ProxyHostId { get; set; }

    public string ForwardHost { get; set; } = string.Empty;

    public int ForwardPort { get; set; } = 80;
}

/// <summary>A path-prefix location under a host, proxied to its own destination.</summary>
public sealed class ProxyLocation
{
    public Guid Id { get; set; }

    public Guid ProxyHostId { get; set; }

    /// <summary>Path prefix, e.g. "/api" or "/api/v2".</summary>
    public string PathPrefix { get; set; } = string.Empty;

    /// <summary>When true, the matched prefix is stripped before forwarding.</summary>
    public bool StripPrefix { get; set; } = true;

    /// <summary>"http" or "https".</summary>
    public string Scheme { get; set; } = "http";

    public string ForwardHost { get; set; } = string.Empty;

    public int ForwardPort { get; set; } = 80;

    /// <summary>Lower values match first; locations always outrank the host's default catch-all route.</summary>
    public int Order { get; set; }
}

/// <summary>A custom header manipulation applied by the proxy pipeline (request or response side).</summary>
public sealed class ProxyHeader
{
    public Guid Id { get; set; }

    public Guid ProxyHostId { get; set; }

    /// <summary>"Request" or "Response".</summary>
    public string Target { get; set; } = "Request";

    /// <summary>"Set", "Append" or "Remove".</summary>
    public string Action { get; set; } = "Set";

    public string Name { get; set; } = string.Empty;

    /// <summary>Value for Set/Append; ignored for Remove.</summary>
    public string Value { get; set; } = string.Empty;
}
