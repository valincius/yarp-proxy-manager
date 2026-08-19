using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProxyManager.Application;
using ProxyManager.Application.Streams;
using ProxyManager.Domain;

namespace ProxyManager.Streams;

public sealed class StreamListenerFactory(
    StreamStatusRegistry statusRegistry,
    ILogger<StreamListenerFactory> logger)
{
    public IStreamListener Create(Domain.Stream stream) =>
        stream.Protocol switch
        {
            StreamProtocol.Udp => new UdpStreamListener(
                stream.Id, stream.ListenPort, stream.ForwardHost, stream.ForwardPort, statusRegistry, logger),
            _ => new TcpStreamListener(
                stream.Id, stream.ListenPort, stream.ForwardHost, stream.ForwardPort, statusRegistry, logger),
        };
}

/// <summary>
/// Keeps the running stream listeners in sync with the database: starts new/disabled listeners,
/// stops removed or changed ones, and records bind failures in the status registry.
/// </summary>
public sealed class StreamHostService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StreamListenerFactory _factory;
    private readonly StreamStatusRegistry _statusRegistry;
    private readonly ILogger<StreamHostService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly Dictionary<Guid, (IStreamListener Listener, Domain.Stream Config)> _listeners = new();

    public StreamHostService(
        IServiceScopeFactory scopeFactory,
        StreamListenerFactory factory,
        StreamStatusRegistry statusRegistry,
        ILogger<StreamHostService> logger)
    {
        _scopeFactory = scopeFactory;
        _factory = factory;
        _statusRegistry = statusRegistry;
        _logger = logger;
    }

    /// <summary>Wakes the sync loop immediately (called after stream config changes).</summary>
    public void RequestSync()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already pending.
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await SyncAsync(stoppingToken);
            var wake = _wake.WaitAsync(stoppingToken);
            var poll = Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            await Task.WhenAny(wake, poll);
        }
    }

    private async Task SyncAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IStreamRepository>();
            var streams = await repository.ListAsync(cancellationToken);
            var enabled = streams.Where(s => s.Enabled).ToDictionary(s => s.Id);

            // Stop listeners for streams that were removed or disabled.
            foreach (var (streamId, entry) in _listeners.ToList())
            {
                if (!enabled.ContainsKey(streamId))
                {
                    await entry.Listener.DisposeAsync();
                    _listeners.Remove(streamId);
                    _statusRegistry.Remove(streamId);
                }
            }

            // Start or restart changed streams.
            foreach (var stream in enabled.Values)
            {
                if (_listeners.TryGetValue(stream.Id, out var entry))
                {
                    if (entry.Listener.Matches(stream))
                    {
                        continue;
                    }

                    await entry.Listener.DisposeAsync();
                    _listeners.Remove(stream.Id);
                }

                var listener = _factory.Create(stream);
                try
                {
                    await listener.StartAsync(cancellationToken);
                    _listeners[stream.Id] = (listener, stream);
                    _logger.LogInformation(
                        "Stream '{Name}' ({Protocol} :{Port}) started.", stream.Name, stream.Protocol, stream.ListenPort);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Bind failure — the listener recorded the error in the status registry.
                    _logger.LogWarning(ex, "Stream '{Name}' failed to start on port {Port}.", stream.Name, stream.ListenPort);
                    await listener.DisposeAsync();
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Stream listener sync failed.");
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed class StreamChangeNotifier(StreamHostService streamHostService) : IConfigReloadNotifier
{
    public void Notify() => streamHostService.RequestSync();
}
