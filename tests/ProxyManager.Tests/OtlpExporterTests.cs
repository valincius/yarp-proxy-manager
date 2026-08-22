using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;
using Xunit;

namespace ProxyManager.Tests;

/// <summary>
/// Verifies the OTLP HTTP exporter actually delivers spans to an endpoint (Sdk and DI
/// registration paths), guarding the distributed-tracing wiring used by Program.cs.
/// </summary>
public sealed class OtlpExporterTests
{
    [Fact]
    public async Task OtlpExporter_DeliversSpans_ToLocalTcpEndpoint()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var received = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[8192];
            // The exporter keeps the connection alive, so wait for the first chunk
            // of the POST rather than EOF.
            var read = await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            received.TrySetResult(read);
        });

        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource("OtlpProbe.Source")
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri($"http://127.0.0.1:{port}");
                options.Protocol = OtlpExportProtocol.HttpProtobuf;
            })
            .Build();

        using var source = new ActivitySource("OtlpProbe.Source");
        using (var activity = source.StartActivity("probe-span"))
        {
            activity?.SetTag("host", "probe.test");
        }

        var bytes = await received.Task.WaitAsync(TimeSpan.FromSeconds(15));
        bytes.Should().BeGreaterThan(0, "the OTLP exporter should POST span data to the endpoint");
    }

    [Fact]
    public async Task OtlpExporter_WithDiRegistration_DeliversSpans()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var received = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[8192];
            var read = await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            received.TrySetResult(read);
        });

        var services = new ServiceCollection();
        services.AddOpenTelemetry().WithTracing(tracing => tracing
            .AddSource("OtlpProbe.DiSource")
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri($"http://127.0.0.1:{port}");
                options.Protocol = OtlpExportProtocol.HttpProtobuf;
            }));
        await using var provider = services.BuildServiceProvider();
        foreach (var hosted in provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }

        using var source = new ActivitySource("OtlpProbe.DiSource");
        using (var activity = source.StartActivity("di-span"))
        {
            activity?.SetTag("host", "probe.test");
        }

        var bytes = await received.Task.WaitAsync(TimeSpan.FromSeconds(15));
        bytes.Should().BeGreaterThan(0, "the DI-registered OTLP exporter should POST span data");
    }
}
