using Microsoft.Extensions.Logging;
using ProxyManager.Application;
using ProxyManager.Application.Certificates;
using ProxyManager.Application.ProxyHosts;
using ProxyManager.Application.Redirects;
using ProxyManager.Domain;

namespace ProxyManager.Certificates;

/// <summary>
/// Enforces one certificate row per normalized domain set. After a certificate is
/// created, uploaded or renewed, any other row covering the same domains is deleted
/// and hosts/redirects referencing it are re-pointed to the survivor so HTTPS and
/// force-HTTPS keep working. Also provides a startup sweep that collapses any
/// duplicates left by earlier versions.
/// </summary>
public sealed class CertificateDeduplicator(
    ICertificateRepository certificates,
    IProxyHostRepository hosts,
    IRedirectHostRepository redirects,
    CertificateFileStore fileStore,
    IConfigReloadNotifier notifier,
    ILogger<CertificateDeduplicator> logger)
{
    /// <summary>
    /// Deletes other certificates with the same normalized domain set as <paramref name="survivor"/>
    /// and re-points host/redirect references to the survivor. No-op when there are no duplicates.
    /// Safe to call on every successful issue/upload/renewal.
    /// </summary>
    public async Task DeduplicateAsync(Certificate survivor, CancellationToken cancellationToken = default)
    {
        var all = await certificates.ListCertificatesAsync(cancellationToken);
        var duplicates = all
            .Where(c => c.Id != survivor.Id && DomainName.SameSet(c.Domains, survivor.Domains))
            .ToList();
        if (duplicates.Count == 0)
        {
            return;
        }

        await RemoveAsync(duplicates, survivor.Id, cancellationToken);
        notifier.Notify();
        logger.LogInformation(
            "Removed {Count} duplicate certificate(s) for domains [{Domains}]; references re-pointed to '{Survivor}'.",
            duplicates.Count,
            string.Join(", ", survivor.Domains),
            survivor.Name);
    }

    /// <summary>
    /// Collapses pre-existing duplicates across all certificates: one row per domain set.
    /// The best candidate per set survives (Issued and unexpired beats other statuses, then
    /// newest creation wins) and references are re-pointed to it.
    /// </summary>
    public async Task SweepAsync(CancellationToken cancellationToken = default)
    {
        var all = await certificates.ListCertificatesAsync(cancellationToken);
        var groups = all.GroupBy(c => string.Join('\u0001', DomainName.Set(c.Domains)));
        var removed = 0;
        foreach (var group in groups.Where(g => g.Count() > 1))
        {
            var ordered = group
                .OrderByDescending(c => c.Status == CertificateStatus.Issued
                    && (c.NotAfter is null || c.NotAfter > DateTimeOffset.UtcNow))
                .ThenByDescending(c => c.CreatedAt)
                .ToList();
            var survivor = ordered[0];
            var duplicates = ordered.Skip(1).ToList();
            await RemoveAsync(duplicates, survivor.Id, cancellationToken);
            removed += duplicates.Count;
        }

        if (removed > 0)
        {
            notifier.Notify();
            logger.LogInformation("Startup certificate sweep removed {Count} duplicate row(s).", removed);
        }
    }

    private async Task RemoveAsync(
        IReadOnlyList<Certificate> duplicates,
        Guid survivorId,
        CancellationToken cancellationToken)
    {
        var hostList = await hosts.ListAsync(cancellationToken);
        var redirectList = await redirects.ListAsync(cancellationToken);
        foreach (var duplicate in duplicates)
        {
            foreach (var host in hostList.Where(h => h.CertificateId == duplicate.Id))
            {
                host.CertificateId = survivorId;
                host.UpdatedAt = DateTimeOffset.UtcNow;
                await hosts.UpdateAsync(host, cancellationToken);
            }

            foreach (var redirect in redirectList.Where(r => r.CertificateId == duplicate.Id))
            {
                redirect.CertificateId = survivorId;
                redirect.UpdatedAt = DateTimeOffset.UtcNow;
                await redirects.UpdateAsync(redirect, cancellationToken);
            }

            await certificates.DeleteCertificateAsync(duplicate, cancellationToken);
            fileStore.Delete(duplicate.Id);
            logger.LogInformation(
                "Deleted duplicate certificate '{Name}' ({Id}); references re-pointed to {SurvivorId}.",
                duplicate.Name,
                duplicate.Id,
                survivorId);
        }
    }
}
