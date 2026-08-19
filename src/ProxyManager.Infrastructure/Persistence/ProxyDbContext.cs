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

    public DbSet<ProxyDestination> ProxyDestinations => Set<ProxyDestination>();

    public DbSet<Certificate> Certificates => Set<Certificate>();

    public DbSet<DnsCredential> DnsCredentials => Set<DnsCredential>();

    public DbSet<AcmeAccount> AcmeAccounts => Set<AcmeAccount>();

    public DbSet<RedirectHost> RedirectHosts => Set<RedirectHost>();

    public DbSet<AccessList> AccessLists => Set<AccessList>();

    public DbSet<AccessListRule> AccessListRules => Set<AccessListRule>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Domain.Stream> Streams => Set<Domain.Stream>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

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
            entity.HasMany(x => x.Destinations).WithOne()
                .HasForeignKey(x => x.ProxyHostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProxyDestination>(entity =>
        {
            entity.ToTable("ProxyDestinations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ForwardHost).HasMaxLength(253).IsRequired();
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

        builder.Entity<Certificate>(entity =>
        {
            entity.ToTable("Certificates");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Domains).HasConversion(
                v => JsonSerializer.Serialize(v),
                v => JsonSerializer.Deserialize<List<string>>(v) ?? new List<string>());
            entity.Property(x => x.PfxPath).HasMaxLength(500);
            entity.Property(x => x.EncryptedPfxPassword).HasMaxLength(2000);
            entity.Property(x => x.ChallengeType).HasMaxLength(10);
            entity.Property(x => x.LastRenewalError).HasMaxLength(2000);
        });

        builder.Entity<DnsCredential>(entity =>
        {
            entity.ToTable("DnsCredentials");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            entity.Property(x => x.EncryptedApiToken).HasMaxLength(4000).IsRequired();
        });

        builder.Entity<AcmeAccount>(entity =>
        {
            entity.ToTable("AcmeAccounts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(200).IsRequired();
            entity.Property(x => x.EncryptedAccountKey).HasMaxLength(8000).IsRequired();
            entity.Property(x => x.DirectoryUrl).HasMaxLength(500).IsRequired();
        });

        builder.Entity<RedirectHost>(entity =>
        {
            entity.ToTable("RedirectHosts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.DomainNames).HasConversion(
                v => JsonSerializer.Serialize(v),
                v => JsonSerializer.Deserialize<List<string>>(v) ?? new List<string>());
            entity.Property(x => x.ForwardScheme).HasMaxLength(10).IsRequired();
            entity.Property(x => x.ForwardHost).HasMaxLength(253).IsRequired();
        });

        builder.Entity<AccessList>(entity =>
        {
            entity.ToTable("AccessLists");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.HasMany(x => x.Rules).WithOne()
                .HasForeignKey(x => x.AccessListId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AccessListRule>(entity =>
        {
            entity.ToTable("AccessListRules");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Action).HasMaxLength(10).IsRequired();
            entity.Property(x => x.Pattern).HasMaxLength(100).IsRequired();
        });

        builder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Details).HasMaxLength(8000);
            entity.HasIndex(x => x.Timestamp);
        });

        builder.Entity<Domain.Stream>(entity =>
        {
            entity.ToTable("Streams");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ForwardHost).HasMaxLength(253).IsRequired();
            entity.HasIndex(x => x.ListenPort).IsUnique();
        });

        builder.Entity<ApiKey>(entity =>
        {
            entity.ToTable("ApiKeys");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.KeyHash).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Prefix).HasMaxLength(20).IsRequired();
            entity.HasIndex(x => x.Prefix).IsUnique();
        });
    }
}
