using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProxyManager.Application;
using ProxyManager.Domain;
using ProxyManager.Infrastructure.Persistence;

namespace ProxyManager.Api.Controllers;

/// <summary>JSON export/restore of the configuration (hosts, redirects, streams, access lists).
/// Certificate private keys are not exported — certificate PFX files must be backed up from disk.
/// Admin-only: API keys cannot export or restore configuration.</summary>
[Route("api/v1/backup")]
[Authorize(Roles = "Admin")]
public sealed class BackupController(
    ProxyDbContext db,
    IConfigReloadNotifier notifier) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var backup = new BackupPayload(
            ExportedAt: DateTimeOffset.UtcNow,
            Hosts: await db.ProxyHosts.AsNoTracking()
                .Include(h => h.Locations)
                .Include(h => h.RequestHeaders)
                .Include(h => h.ResponseHeaders)
                .Include(h => h.Destinations)
                .ToListAsync(cancellationToken),
            Redirects: await db.RedirectHosts.AsNoTracking().ToListAsync(cancellationToken),
            Streams: await db.Streams.AsNoTracking().ToListAsync(cancellationToken),
            AccessLists: await db.AccessLists.AsNoTracking().Include(a => a.Rules).ToListAsync(cancellationToken));

        return Ok(backup);
    }

    [HttpPost("restore")]
    public async Task<IActionResult> Restore(BackupPayload payload, CancellationToken cancellationToken)
    {
        // Replace the existing configuration with the backup (certificate files are not restored).
        db.ProxyHosts.RemoveRange(await db.ProxyHosts.ToListAsync(cancellationToken));
        db.RedirectHosts.RemoveRange(await db.RedirectHosts.ToListAsync(cancellationToken));
        db.Streams.RemoveRange(await db.Streams.ToListAsync(cancellationToken));
        db.AccessLists.RemoveRange(await db.AccessLists.ToListAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);

        if (payload.Hosts is not null)
        {
            db.ProxyHosts.AddRange(payload.Hosts);
        }

        if (payload.Redirects is not null)
        {
            db.RedirectHosts.AddRange(payload.Redirects);
        }

        if (payload.Streams is not null)
        {
            db.Streams.AddRange(payload.Streams);
        }

        if (payload.AccessLists is not null)
        {
            db.AccessLists.AddRange(payload.AccessLists);
        }

        await db.SaveChangesAsync(cancellationToken);
        notifier.Notify();
        return Ok();
    }
}

public sealed record BackupPayload(
    DateTimeOffset ExportedAt,
    IReadOnlyList<ProxyHost>? Hosts,
    IReadOnlyList<RedirectHost>? Redirects,
    IReadOnlyList<Domain.Stream>? Streams,
    IReadOnlyList<AccessList>? AccessLists);
