using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using ProxyManager.Domain;

namespace ProxyManager.Streams;

/// <summary>TCP forwarder: accept → bidirectional relay to the upstream.</summary>
public sealed class TcpStreamListener : IStreamListener
{
    private readonly Guid _streamId;
    private readonly int _listenPort;
    private readonly string _forwardHost;
    private readonly int _forwardPort;
    private readonly StreamStatusRegistry _statusRegistry;
    private readonly ILogger _logger;
    private readonly int _maxSessions = 1000;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private bool _started;
    private int _activeSessions;
    private long _bytesIn;
    private long _bytesOut;
    private string? _error;

    public TcpStreamListener(
        Guid streamId,
        int listenPort,
        string forwardHost,
        int forwardPort,
        StreamStatusRegistry statusRegistry,
        ILogger logger)
    {
        _streamId = streamId;
        _listenPort = listenPort;
        _forwardHost = forwardHost;
        _forwardPort = forwardPort;
        _statusRegistry = statusRegistry;
        _logger = logger;
    }

    public bool Matches(Domain.Stream stream) =>
        stream.Id == _streamId
        && stream.Protocol == StreamProtocol.Tcp
        && stream.ListenPort == _listenPort
        && stream.ForwardHost == _forwardHost
        && stream.ForwardPort == _forwardPort;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, _listenPort);
            _listener.Start();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _started = true;
            _ = AcceptLoopAsync(_cts.Token);
            _error = null;
            PublishStatus();
        }
        catch (Exception ex)
        {
            _started = false;
            _error = ex.Message;
            PublishStatus();
            throw;
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }

            if (Volatile.Read(ref _activeSessions) >= _maxSessions)
            {
                client.Dispose();
                continue;
            }

            _ = HandleClientAsync(client, cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _activeSessions);
        PublishStatus();
        try
        {
            using (client)
            using (var upstream = new TcpClient())
            {
                await upstream.ConnectAsync(_forwardHost, _forwardPort, cancellationToken);
                var clientStream = client.GetStream();
                var upstreamStream = upstream.GetStream();

                var toUpstream = RelayAsync(clientStream, upstreamStream, read =>
                    Interlocked.Add(ref _bytesIn, read));
                var toClient = RelayAsync(upstreamStream, clientStream, read =>
                    Interlocked.Add(ref _bytesOut, read));

                await Task.WhenAny(toUpstream, toClient);
                PublishStatus();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "TCP stream session ended with an error.");
        }
        finally
        {
            Interlocked.Decrement(ref _activeSessions);
            PublishStatus();
        }
    }

    private static async Task RelayAsync(System.IO.Stream source, System.IO.Stream destination, Action<int> count)
    {
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            count(read);
            await destination.WriteAsync(buffer.AsMemory(0, read));
            await destination.FlushAsync();
        }
    }

    private void PublishStatus() =>
        _statusRegistry.Set(_streamId, new StreamStatus(
            _started,
            _error,
            Volatile.Read(ref _activeSessions),
            Interlocked.Read(ref _bytesIn),
            Interlocked.Read(ref _bytesOut),
            DateTimeOffset.UtcNow));

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _cts?.Dispose();
        _started = false;
        PublishStatus();
        await Task.CompletedTask;
    }
}
