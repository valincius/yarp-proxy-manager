using ProxyManager.Domain;

namespace ProxyManager.Application.Settings;

public interface ISettingRepository
{
    Task<Setting?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Setting>> ListAsync(CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
}
