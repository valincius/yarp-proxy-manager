using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ProxyManager.Domain;

namespace ProxyManager.Infrastructure.Persistence;

public sealed class ProxyDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public ProxyDbContext(DbContextOptions<ProxyDbContext> options)
        : base(options)
    {
    }

    public DbSet<ProxyHost> ProxyHosts => Set<ProxyHost>();

    public DbSet<ProxyLocation> ProxyLocations => Set<ProxyLocation>();

    public DbSet<ProxyHeader> ProxyHeaders => Set<ProxyHeader>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ProxyHost>(entity =>
        {
            entity.ToTable("ProxyHosts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.DomainNames).HasConversion(
                v => JsonSerializer.Serialize(v),
                v => JsonSerializer.Deserialize<List<string>>(v) ?? new List<string>());
            entity.Property(x => x.Scheme).HasMaxLength(10).IsRequired();
            entity.Property(x => x.ForwardHost).HasMaxLength(253).IsRequired();
            entity.HasMany(x => x.Locations).WithOne()
                .HasForeignKey(x => x.ProxyHostId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.RequestHeaders).WithOne()
                .HasForeignKey(x => x.ProxyHostId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.ResponseHeaders).WithOne()
                .HasForeignKey(x => x.ProxyHostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProxyLocation>(entity =>
        {
            entity.ToTable("ProxyLocations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PathPrefix).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Scheme).HasMaxLength(10).IsRequired();
            entity.Property(x => x.ForwardHost).HasMaxLength(253).IsRequired();
        });

        builder.Entity<ProxyHeader>(entity =>
        {
            entity.ToTable("ProxyHeaders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Target).HasMaxLength(10).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(10).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Value).HasMaxLength(2000);
        });
    }
}
