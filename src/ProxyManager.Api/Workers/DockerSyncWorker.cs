using ProxyManager.Application.Settings;
using ProxyManager.Infrastructure.Docker;

namespace ProxyManager.Api.Workers;

/// <summary>
/// Periodically runs Docker label autodiscovery (traefik-style). No-op while the
/// Docker integration is disabled. Each cycle runs in its own scope.
/// </summary>
public sealed class DockerSyncWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<DockerSyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await SyncOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Docker autodiscovery cycle failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SyncOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        if (await settings.GetAsync("Docker:Enabled", cancellationToken) != "true")
        {
            return;
        }

        var sync = scope.ServiceProvider.GetRequiredService<DockerHostSyncService>();
        await sync.SyncAsync(cancellationToken);
    }
}
