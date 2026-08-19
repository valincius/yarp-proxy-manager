using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ProxyManager.Domain;

namespace ProxyManager.Infrastructure.Persistence;

/// <summary>
/// Writes an immutable AuditLog entry for every added/modified/deleted configuration entity.
/// Registered on the DbContext options; the actor comes from the ambient HTTP context.
/// </summary>
public sealed class AuditSaveChangesInterceptor(
    IHttpContextAccessor httpContextAccessor,
    TimeProvider time) : SaveChangesInterceptor
{
    private static readonly HashSet<string> AuditedEntityTypes =
    [
        nameof(ProxyHost),
        nameof(RedirectHost),
        nameof(AccessList),
        nameof(Certificate),
        nameof(DnsCredential),
        nameof(AcmeAccount),
    ];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        WriteAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        WriteAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void WriteAuditEntries(DbContext? context)
    {
        if (context is not ProxyDbContext db)
        {
            return;
        }

        var userId = httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true
            ? TryGetUserId(httpContextAccessor.HttpContext.User)
            : null;

        var now = time.GetUtcNow();
        var auditEntries = new List<AuditLog>();
        foreach (var entry in db.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            var entityType = entry.Entity.GetType().Name;
            if (!AuditedEntityTypes.Contains(entityType))
            {
                continue;
            }

            var entityId = entry.Property("Id")?.CurrentValue?.ToString();
            var details = entry.State == EntityState.Deleted
                ? "{}"
                : JsonSerializer.Serialize(entry.Properties
                    .Where(p => p.IsModified && p.Metadata.Name != "UpdatedAt")
                    .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue?.ToString() ?? string.Empty));

            auditEntries.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                Timestamp = now,
                UserId = userId,
                EntityType = entityType,
                EntityId = Guid.TryParse(entityId, out var parsed) ? parsed : null,
                Action = entry.State.ToString(),
                Details = details,
            });
        }

        // Add after the loop: mutating the change tracker while enumerating it throws.
        foreach (var audit in auditEntries)
        {
            db.AuditLogs.Add(audit);
        }
    }

    private static Guid? TryGetUserId(System.Security.Claims.ClaimsPrincipal user)
    {
        var claim = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
