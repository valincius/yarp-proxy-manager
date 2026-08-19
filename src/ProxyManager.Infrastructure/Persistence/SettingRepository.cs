using Microsoft.EntityFrameworkCore;
using ProxyManager.Application.Settings;
using ProxyManager.Domain;

namespace ProxyManager.Infrastructure.Persistence;

public sealed class SettingRepository(ProxyDbContext db) : ISettingRepository
{
    public async Task<Setting?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

    public async Task<IReadOnlyList<Setting>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Settings.AsNoTracking().ToListAsync(cancellationToken);

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var existing = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (existing is null)
        {
            db.Settings.Add(new Setting { Key = key, Value = value });
        }
        else
        {
            existing.Value = value;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
