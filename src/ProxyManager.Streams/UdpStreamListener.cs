using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using ProxyManager.Domain;

namespace ProxyManager.Streams;

/// <summary>UDP forwarder: relays datagrams between clients and the upstream, per client endpoint.</summary>
public sealed class UdpStreamListener : IStreamListener
{
    private static readonly TimeSpan SessionIdleTimeout = TimeSpan.FromMinutes(5);

    private readonly Guid _streamId;
    private readonly int _listenPort;
    private readonly string _forwardHost;
    private readonly int _forwardPort;
    private readonly StreamStatusRegistry _statusRegistry;
    private readonly ILogger _logger;

    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private readonly Dictionary<IPEndPoint, UdpSession> _sessions = new();
    private readonly object _sync = new();
    private long _bytesIn;
    private long _bytesOut;
    private string? _error;

    public UdpStreamListener(
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
        && stream.Protocol == StreamProtocol.Udp
        && stream.ListenPort == _listenPort
        && stream.ForwardHost == _forwardHost
        && stream.ForwardPort == _forwardPort;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _udp = new UdpClient(_listenPort);
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _ = ReceiveLoopAsync(_cts.Token);
            _ = CleanupLoopAsync(_cts.Token);
            _error = null;
            PublishStatus();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            PublishStatus();
            throw;
        }

        await Task.CompletedTask;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _udp!.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }

            var session = GetOrCreateSession(result.RemoteEndPoint);
            Interlocked.Add(ref _bytesIn, result.Buffer.Length);
            try
            {
                await session.Upstream.SendAsync(result.Buffer, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "UDP forward failed for {Endpoint}.", result.RemoteEndPoint);
            }
        }
    }

    private UdpSession GetOrCreateSession(IPEndPoint remoteEndPoint)
    {
        lock (_sync)
        {
            if (_sessions.TryGetValue(remoteEndPoint, out var existing))
            {
                existing.LastActive = DateTimeOffset.UtcNow;
                return existing;
            }

            var upstream = new UdpClient();
            upstream.Connect(_forwardHost, _forwardPort);
            var session = new UdpSession(upstream, remoteEndPoint);
            _sessions[remoteEndPoint] = session;
            _ = UpstreamLoopAsync(session, _cts!.Token);
            return session;
        }
    }

    private async Task UpstreamLoopAsync(UdpSession session, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await session.Upstream.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }

            Interlocked.Add(ref _bytesOut, result.Buffer.Length);
            try
            {
                await _udp!.SendAsync(result.Buffer, session.RemoteEndPoint, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "UDP reply failed for {Endpoint}.", session.RemoteEndPoint);
            }
        }
    }

    private async Task CleanupLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var cutoff = DateTimeOffset.UtcNow - SessionIdleTimeout;
            lock (_sync)
            {
                foreach (var (endpoint, session) in _sessions.Where(s => s.Value.LastActive < cutoff).ToList())
                {
                    _sessions.Remove(endpoint);
                    session.Upstream.Dispose();
                }
            }
        }
    }

    private void PublishStatus() =>
        _statusRegistry.Set(_streamId, new StreamStatus(
            _udp is { Client.Connected: true },
            _error,
            _sessions.Count,
            Interlocked.Read(ref _bytesIn),
            Interlocked.Read(ref _bytesOut),
            DateTimeOffset.UtcNow));

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        lock (_sync)
        {
            foreach (var session in _sessions.Values)
            {
                session.Upstream.Dispose();
            }

            _sessions.Clear();
        }

        _udp?.Dispose();
        _cts?.Dispose();
        await Task.CompletedTask;
    }

    private sealed class UdpSession(UdpClient upstream, IPEndPoint remoteEndPoint)
    {
        public UdpClient Upstream { get; } = upstream;

        public IPEndPoint RemoteEndPoint { get; } = remoteEndPoint;

        public DateTimeOffset LastActive { get; set; } = DateTimeOffset.UtcNow;
    }
}
