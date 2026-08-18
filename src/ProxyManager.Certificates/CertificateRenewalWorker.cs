using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProxyManager.Domain;

namespace ProxyManager.Certificates;

/// <summary>
/// Background renewal: periodically re-issues ACME certificates that expire soon.
/// Failed renewals keep the previous certificate serving and are retried next pass.
/// </summary>
public sealed class CertificateRenewalWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<CertificateRenewalWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RenewalWindow = TimeSpan.FromDays(30);
    private static readonly TimeSpan MinimumRetryInterval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunPassAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(12));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunPassAsync(stoppingToken);
        }
    }

    private async Task RunPassAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<CertificateManager>();
            var due = await manager.FindRenewableAsync(stoppingToken);

            foreach (var certificate in due)
            {
                stoppingToken.ThrowIfCancellationRequested();
                try
                {
                    await manager.RenewAsync(certificate.Id, stoppingToken);
                    logger.LogInformation("Renewed certificate '{Name}' (expires {Expires}).",
                        certificate.Name, certificate.NotAfter?.ToString("O"));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Renewal failed for certificate '{Name}'.", certificate.Name);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Certificate renewal pass failed.");
        }
    }

    /// <summary>Certificates due for renewal: ACME-issued, expiring within the window, not retried recently.</summary>
    public static bool IsDue(Certificate certificate, DateTimeOffset now)
    {
        if (certificate.Provider != CertificateProvider.Acme || certificate.Status == CertificateStatus.Revoked)
        {
            return false;
        }

        var expiresSoon = certificate.NotAfter is null || certificate.NotAfter <= now + RenewalWindow;
        if (!expiresSoon)
        {
            return false;
        }

        return certificate.LastRenewalAttempt is null
            || certificate.LastRenewalAttempt <= now - MinimumRetryInterval;
    }
}
