using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProxyManager.Infrastructure.Persistence;

/// <summary>Used by `dotnet ef migrations` — not part of the runtime pipeline.</summary>
public sealed class ProxyDbContextFactory : IDesignTimeDbContextFactory<ProxyDbContext>
{
    public ProxyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ProxyDbContext>()
            .UseSqlite("Data Source=data/design-time.db")
            .Options;

        return new ProxyDbContext(options);
    }
}
