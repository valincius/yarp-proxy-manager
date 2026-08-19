using System.Collections.Concurrent;
using ProxyManager.Domain;

namespace ProxyManager.Application.Settings;

/// <summary>
/// Key/value settings with an in-process cache. Writers update the cache
/// immediately so readers (e.g. the proxy 404-page middleware) observe
/// changes without a restart. Single-instance deployment assumption.
/// </summary>
public sealed class SettingsService(ISettingRepository repository)
{
    private const string NotFoundModeKey = "NotFound:Mode";
    private const string NotFoundTemplateKey = "NotFound:Template";

    private readonly ConcurrentDictionary<string, string> _cache = new();

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var setting = await repository.GetAsync(key, cancellationToken);
        var value = setting?.Value;
        if (value is not null)
        {
            _cache[key] = value;
        }

        return value;
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await repository.SetAsync(key, value, cancellationToken);
        _cache[key] = value;
    }

    public async Task<NotFoundSettingsDto> GetNotFoundSettingsAsync(CancellationToken cancellationToken = default)
    {
        var mode = await GetAsync(NotFoundModeKey, cancellationToken) ?? NotFoundModes.Default;
        if (mode is not NotFoundModes.Default and not NotFoundModes.Empty and not NotFoundModes.Custom)
        {
            mode = NotFoundModes.Default;
        }

        var template = mode == NotFoundModes.Custom
            ? await GetAsync(NotFoundTemplateKey, cancellationToken) ?? string.Empty
            : string.Empty;

        return new NotFoundSettingsDto(mode, template);
    }

    public async Task<NotFoundSettingsDto> SetNotFoundSettingsAsync(
        NotFoundSettingsInput input,
        CancellationToken cancellationToken = default)
    {
        var mode = input.Mode is NotFoundModes.Empty or NotFoundModes.Custom ? input.Mode : NotFoundModes.Default;

        await SetAsync(NotFoundModeKey, mode, cancellationToken);
        if (mode == NotFoundModes.Custom)
        {
            await SetAsync(NotFoundTemplateKey, input.Template ?? string.Empty, cancellationToken);
        }

        return await GetNotFoundSettingsAsync(cancellationToken);
    }

    private const string DockerEnabledKey = "Docker:Enabled";
    private const string DockerHostKey = "Docker:Host";
    private const string DockerNetworkKey = "Docker:Network";
    private const string DockerLastSyncKey = "Docker:LastSyncAt";
    private const string DockerLastErrorKey = "Docker:LastError";
    private const string DockerManagedHostsKey = "Docker:ManagedHosts";
    private const string DockerDiscoveredKey = "Docker:DiscoveredContainers";

    public async Task<DockerSettingsDto> GetDockerSettingsAsync(CancellationToken cancellationToken = default)
    {
        var enabled = await GetAsync(DockerEnabledKey, cancellationToken) == "true";
        var host = await GetAsync(DockerHostKey, cancellationToken);
        var network = await GetAsync(DockerNetworkKey, cancellationToken);
        var lastSyncAt = ParseDate(await GetAsync(DockerLastSyncKey, cancellationToken));
        var lastError = await GetAsync(DockerLastErrorKey, cancellationToken);
        var managedHosts = ParseInt(await GetAsync(DockerManagedHostsKey, cancellationToken));
        var discovered = ParseInt(await GetAsync(DockerDiscoveredKey, cancellationToken));

        return new DockerSettingsDto(enabled, host, network, lastSyncAt, lastError, managedHosts, discovered);
    }

    public async Task SetDockerSettingsAsync(
        DockerSettingsInput input,
        CancellationToken cancellationToken = default)
    {
        await SetAsync(DockerEnabledKey, input.Enabled ? "true" : "false", cancellationToken);
        if (!string.IsNullOrWhiteSpace(input.Host))
        {
            await SetAsync(DockerHostKey, input.Host.Trim(), cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(input.Network))
        {
            await SetAsync(DockerNetworkKey, input.Network.Trim(), cancellationToken);
        }
    }

    /// <summary>Records the outcome of the most recent Docker discovery pass.</summary>
    public async Task SetDockerStatusAsync(
        DateTimeOffset? lastSyncAt,
        string? lastError,
        int managedHosts,
        int discoveredContainers,
        CancellationToken cancellationToken = default)
    {
        await SetAsync(DockerLastSyncKey, lastSyncAt?.ToString("O") ?? string.Empty, cancellationToken);
        await SetAsync(DockerLastErrorKey, lastError ?? string.Empty, cancellationToken);
        await SetAsync(DockerManagedHostsKey, managedHosts.ToString(), cancellationToken);
        await SetAsync(DockerDiscoveredKey, discoveredContainers.ToString(), cancellationToken);
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static int ParseInt(string? value) =>
        int.TryParse(value, out var parsed) ? parsed : 0;
}
