using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProxyManager.Application.Proxy;
using Yarp.ReverseProxy.Configuration;

namespace ProxyManager.Proxy;

/// <summary>
/// Rebuilds the YARP route/cluster set from the database and swaps it atomically via
/// <see cref="InMemoryConfigProvider"/>. Reloads are serialized and coalesced so bursts of
/// writes trigger a single refresh.
/// </summary>
public sealed class ProxyConfigReloader
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly InMemoryConfigProvider _provider;
    private readonly ILogger<ProxyConfigReloader> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private bool _pending;

    public ProxyConfigReloader(
        IServiceScopeFactory scopeFactory,
        InMemoryConfigProvider provider,
        ILogger<ProxyConfigReloader> logger)
    {
        _scopeFactory = scopeFactory;
        _provider = provider;
        _logger = logger;
    }

    /// <summary>Direct, awaitable reload — used at startup.</summary>
    public Task ReloadAsync(CancellationToken cancellationToken = default)
        => ReloadCoreAsync(cancellationToken);

    /// <summary>Fire-and-forget reload for post-write notifications; coalesces concurrent requests.</summary>
    public void RequestReload()
    {
        lock (_sync)
        {
            _pending = true;
        }

        _ = Task.Run(ReloadLoopAsync);
    }

    private async Task ReloadLoopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            while (true)
            {
                lock (_sync)
                {
                    if (!_pending)
                    {
                        return;
                    }

                    _pending = false;
                }

                await ReloadCoreAsync(CancellationToken.None);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ReloadCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IProxyConfigStore>();
            var hosts = await store.GetEnabledHostsAsync(cancellationToken);
            var (routes, clusters) = YarpConfigBuilder.Build(hosts);
            _provider.Update(routes, clusters);
            _logger.LogInformation(
                "Proxy configuration reloaded: {RouteCount} routes, {ClusterCount} clusters.",
                routes.Count,
                clusters.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep the previous configuration serving; log and let the next notify retry.
            _logger.LogError(ex, "Failed to reload proxy configuration.");
        }
    }
}
