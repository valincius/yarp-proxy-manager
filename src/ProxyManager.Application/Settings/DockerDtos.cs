namespace ProxyManager.Application.Settings;

/// <summary>Docker autodiscovery configuration and last-sync status.</summary>
public sealed record DockerSettingsDto(
    bool Enabled,
    string? Host,
    string? Network,
    DateTimeOffset? LastSyncAt,
    string? LastError,
    int ManagedHosts,
    int DiscoveredContainers);

public sealed record DockerSettingsInput(bool Enabled, string? Host, string? Network);
