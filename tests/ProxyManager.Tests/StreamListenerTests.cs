using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ProxyManager.Application.Streams;
using ProxyManager.Domain;
using ProxyManager.Streams;
using Xunit;

namespace ProxyManager.Tests;

public sealed class TcpStreamListenerTests : IAsyncDisposable
{
    private readonly StreamStatusRegistry _registry = new();

    [Fact]
    public async Task RelaysBytesBetweenClientAndUpstream()
    {
        await using var upstream = await TcpEchoServer.StartAsync();

        var listener = new TcpStreamListener(
            Guid.NewGuid(), 52101, "127.0.0.1", upstream.Port, _registry, NullLogger.Instance);
        await listener.StartAsync(CancellationToken.None);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", 52101);
        var stream = client.GetStream();

        var payload = "hello-tcp"u8.ToArray();
        await stream.WriteAsync(payload);
        await stream.FlushAsync();

        var buffer = new byte[64];
        var read = await stream.ReadAsync(buffer);
        Encoding.UTF8.GetString(buffer, 0, read).Should().Be("hello-tcp");

        await listener.DisposeAsync();
    }

    public async ValueTask DisposeAsync() => await Task.CompletedTask;

    private sealed class TcpEchoServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();

        private TcpEchoServer(TcpListener listener) => _listener = listener;

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public static async Task<TcpEchoServer> StartAsync()
        {
            var server = new TcpEchoServer(new TcpListener(IPAddress.Loopback, 0));
            server._listener.Start();
            _ = server.AcceptLoopAsync(server._cts.Token);
            return server;
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                _ = EchoAsync(client, cancellationToken);
            }
        }

        private static async Task EchoAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                var stream = client.GetStream();
                var buffer = new byte[8192];
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class UdpStreamListenerTests : IAsyncDisposable
{
    private readonly StreamStatusRegistry _registry = new();

    [Fact]
    public async Task RelaysDatagramsInBothDirections()
    {
        var upstream = new UdpClient(0);
        var upstreamEndPoint = (IPEndPoint)upstream.Client.LocalEndPoint!;

        var listener = new UdpStreamListener(
            Guid.NewGuid(), 52102, "127.0.0.1", upstreamEndPoint.Port, _registry, NullLogger.Instance);
        await listener.StartAsync(CancellationToken.None);

        var client = new UdpClient();
        await client.SendAsync("ping-udp"u8.ToArray(), "127.0.0.1", 52102);

        // The upstream receives the datagram; echo it back.
        var received = await upstream.ReceiveAsync();
        Encoding.UTF8.GetString(received.Buffer).Should().Be("ping-udp");
        await upstream.SendAsync(received.Buffer, received.RemoteEndPoint);

        var echoed = await client.ReceiveAsync();
        Encoding.UTF8.GetString(echoed.Buffer).Should().Be("ping-udp");

        await listener.DisposeAsync();
        client.Dispose();
        upstream.Dispose();
    }

    public async ValueTask DisposeAsync() => await Task.CompletedTask;
}

public sealed class StreamServiceTests
{
    [Fact]
    public async Task PortConflicts_AreRejected()
    {
        var repository = new InMemoryStreamRepository();
        repository.Streams.Add(new Domain.Stream { Id = Guid.NewGuid(), Name = "Existing", ListenPort = 53000, ForwardHost = "10.0.0.1", ForwardPort = 1 });
        var service = new StreamService(
            repository,
            new FixedReservedPorts([80, 443, 81]),
            new NoopReloadNotifier(),
            new ProxyManager.Application.Streams.StreamValidator());

        var reserved = async () => await service.CreateAsync(new ProxyManager.Application.Streams.StreamInput(
            "Bad", true, "Tcp", 80, "10.0.0.2", 8080), CancellationToken.None);
        await reserved.Should().ThrowAsync<ProxyManager.Application.Exceptions.DomainConflictException>();

        var duplicate = async () => await service.CreateAsync(new ProxyManager.Application.Streams.StreamInput(
            "Bad", true, "Tcp", 53000, "10.0.0.2", 8080), CancellationToken.None);
        await duplicate.Should().ThrowAsync<ProxyManager.Application.Exceptions.DomainConflictException>();

        var ok = await service.CreateAsync(new ProxyManager.Application.Streams.StreamInput(
            "Good", true, "Udp", 53100, "10.0.0.2", 8080), CancellationToken.None);
        ok.ListenPort.Should().Be(53100);
    }

    private sealed class InMemoryStreamRepository : IStreamRepository
    {
        public List<Domain.Stream> Streams { get; } = [];

        public Task<IReadOnlyList<Domain.Stream>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Domain.Stream>>(Streams.ToList());

        public Task<Domain.Stream?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Streams.FirstOrDefault(s => s.Id == id));

        public Task AddAsync(Domain.Stream stream, CancellationToken cancellationToken = default)
        {
            Streams.Add(stream);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Domain.Stream stream, CancellationToken cancellationToken = default)
        {
            var index = Streams.FindIndex(s => s.Id == stream.Id);
            if (index >= 0)
            {
                Streams[index] = stream;
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(Domain.Stream stream, CancellationToken cancellationToken = default)
        {
            Streams.RemoveAll(s => s.Id == stream.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedReservedPorts(int[] ports) : IReservedPortsProvider
    {
        public IReadOnlyList<int> Ports => ports;
    }
}
