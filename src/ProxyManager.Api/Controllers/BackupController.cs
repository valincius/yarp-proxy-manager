using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProxyManager.Application;
using ProxyManager.Application.Redirects;
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
            SchemaVersion: 1,
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

    [HttpPost("validate")]
    public IActionResult Validate(BackupPayload? payload)
    {
        var errors = ValidatePayload(payload);
        return errors.Count == 0 ? Ok(new { valid = true }) : BadRequest(new { valid = false, errors });
    }

    [HttpPost("restore")]
    public async Task<IActionResult> Restore(BackupPayload payload, CancellationToken cancellationToken)
    {
        var errors = ValidatePayload(payload);
        if (errors.Count > 0)
        {
            return BadRequest(new { valid = false, errors });
        }

        // Replace the existing configuration atomically. Certificate files are not restored.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.ProxyHosts.RemoveRange(await db.ProxyHosts.ToListAsync(cancellationToken));
        db.RedirectHosts.RemoveRange(await db.RedirectHosts.ToListAsync(cancellationToken));
        db.Streams.RemoveRange(await db.Streams.ToListAsync(cancellationToken));
        db.AccessLists.RemoveRange(await db.AccessLists.ToListAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);

        db.ProxyHosts.AddRange(payload.Hosts ?? []);
        db.RedirectHosts.AddRange(payload.Redirects ?? []);
        db.Streams.AddRange(payload.Streams ?? []);
        db.AccessLists.AddRange(payload.AccessLists ?? []);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        notifier.Notify();
        return NoContent();
    }

    private static List<string> ValidatePayload(BackupPayload? payload)
    {
        var errors = new List<string>();
        if (payload is null)
        {
            return ["A backup payload is required."];
        }

        if (payload.SchemaVersion is not 0 and not 1)
        {
            errors.Add($"Unsupported backup schema version {payload.SchemaVersion}.");
        }

        var hosts = payload.Hosts ?? [];
        var redirects = payload.Redirects ?? [];
        var streams = payload.Streams ?? [];
        var accessLists = payload.AccessLists ?? [];

        if (hosts.Select(h => h.Id).Distinct().Count() != hosts.Count)
        {
            errors.Add("Proxy host IDs must be unique.");
        }

        if (redirects.Select(r => r.Id).Distinct().Count() != redirects.Count)
        {
            errors.Add("Redirect host IDs must be unique.");
        }

        if (streams.Select(s => s.Id).Distinct().Count() != streams.Count)
        {
            errors.Add("Stream IDs must be unique.");
        }

        if (accessLists.Select(a => a.Id).Distinct().Count() != accessLists.Count)
        {
            errors.Add("Access-list IDs must be unique.");
        }

        if (hosts.Any(h => h.DomainNames.Count == 0 || h.ForwardPort is < 1 or > 65535))
        {
            errors.Add("Every proxy host must have a domain and a valid forward port.");
        }

        if (redirects.Any(r => r.DomainNames.Count == 0 || r.StatusCode is < 300 or > 308 || r.ForwardPort is < 1 or > 65535))
        {
            errors.Add("Every redirect must have a domain, a status code between 300 and 308, and a valid forward port.");
        }

        if (streams.Any(s => s.ListenPort is < 1 or > 65535 || s.ForwardPort is < 1 or > 65535))
        {
            errors.Add("Every stream must have valid listen and forward ports.");
        }

        if (accessLists.SelectMany(a => a.Rules).Any(r => r.Action is not ("Allow" or "Deny") || (r.Pattern != "*" && !IpPattern.IsValid(r.Pattern))))
        {
            errors.Add("Access-list rules must use Allow/Deny and valid IP, CIDR, or '*' patterns.");
        }

        return errors;
    }
}

public sealed record BackupPayload(
    int SchemaVersion,
    DateTimeOffset ExportedAt,
    IReadOnlyList<ProxyHost>? Hosts,
    IReadOnlyList<RedirectHost>? Redirects,
    IReadOnlyList<Domain.Stream>? Streams,
    IReadOnlyList<AccessList>? AccessLists);
