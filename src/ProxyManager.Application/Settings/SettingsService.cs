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
}
