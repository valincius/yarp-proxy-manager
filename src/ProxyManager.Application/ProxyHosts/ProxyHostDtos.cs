namespace ProxyManager.Application.ProxyHosts;

public sealed record ProxyHostInput(
    string Name,
    IReadOnlyList<string> DomainNames,
    bool Enabled,
    string Scheme,
    string ForwardHost,
    int ForwardPort,
    bool BlockCommonExploits,
    bool ForceHttps,
    Guid? CertificateId,
    Guid? AccessListId,
    IReadOnlyList<ProxyHeaderInput> RequestHeaders,
    IReadOnlyList<ProxyHeaderInput> ResponseHeaders,
    IReadOnlyList<ProxyLocationInput> Locations,
    IReadOnlyList<ProxyDestinationInput> Destinations,
    string? LoadBalancingPolicy,
    bool HealthCheckEnabled,
    string? HealthCheckPath,
    int HealthCheckIntervalSeconds);

public sealed record ProxyDestinationInput(string ForwardHost, int ForwardPort);

public sealed record ProxyLocationInput(
    string PathPrefix,
    bool StripPrefix,
    string Scheme,
    string ForwardHost,
    int ForwardPort,
    int Order);

public sealed record ProxyHeaderInput(
    string Target,
    string Action,
    string Name,
    string Value);

/// <summary>Read-model of a host as the proxy pipeline needs it.</summary>
public sealed record HostConfig(
    Guid Id,
    string[] Domains,
    string Scheme,
    string ForwardHost,
    int ForwardPort,
    bool BlockCommonExploits,
    bool ForceHttps,
    bool CertificateValid,
    Guid? AccessListId,
    IReadOnlyList<DestinationConfig> Destinations,
    string? LoadBalancingPolicy,
    bool HealthCheckEnabled,
    string? HealthCheckPath,
    int HealthCheckIntervalSeconds,
    IReadOnlyList<LocationConfig> Locations,
    IReadOnlyList<HeaderConfig> RequestHeaders,
    IReadOnlyList<HeaderConfig> ResponseHeaders);

public sealed record DestinationConfig(string ForwardHost, int ForwardPort);

public sealed record LocationConfig(
    string PathPrefix,
    bool StripPrefix,
    string Scheme,
    string ForwardHost,
    int ForwardPort);

public sealed record HeaderConfig(
    string Target,
    string Action,
    string Name,
    string Value);
