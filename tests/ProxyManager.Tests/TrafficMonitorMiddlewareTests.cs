using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ProxyManager.Application.Settings;
using ProxyManager.Domain;
using ProxyManager.Proxy;
using Xunit;

namespace ProxyManager.Tests;

public sealed class TrafficMonitorMiddlewareTests
{
    private sealed class InMemorySettingRepository : ISettingRepository
    {
        private readonly ConcurrentDictionary<string, string> _values = new();

        public Task<Setting?> GetAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_values.TryGetValue(key, out var value) ? new Setting { Key = key, Value = value } : null);

        public Task<IReadOnlyList<Setting>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Setting>>(_values.Select(kv => new Setting { Key = kv.Key, Value = kv.Value }).ToList());

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }
    }

    private static DefaultHttpContext CreateContext(SettingsService settings, string host = "app.example.com")
    {
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection().AddSingleton(settings).BuildServiceProvider();
        context.Request.Host = new HostString(host);
        context.Request.Method = "GET";
        context.Request.Path = "/hello";
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.1.2.3");
        return context;
    }

    private static async Task<SettingsService> CreateSettingsAsync(bool captureEnabled, int captureSize)
    {
        var settings = new SettingsService(new InMemorySettingRepository());
        if (captureEnabled)
        {
            await settings.SetAsync(DiagnosticsSettings.CaptureEnabledKey, "true", CancellationToken.None);
            await settings.SetAsync(DiagnosticsSettings.CaptureSizeKey, captureSize.ToString(), CancellationToken.None);
        }

        return settings;
    }

    [Fact]
    public async Task InvokeAsync_RecordsStatusDurationBytesAndClientIp()
    {
        var monitor = new TrafficMonitor();
        var settings = await CreateSettingsAsync(captureEnabled: false, captureSize: 0);
        var middleware = new TrafficMonitorMiddleware(
            ctx =>
            {
                ctx.Response.StatusCode = 201;
                return Task.CompletedTask;
            },
            monitor);

        var context = CreateContext(settings);
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);

        var sample = monitor.RecentRequests(1).Should().ContainSingle().Subject;
        sample.Host.Should().Be("app.example.com");
        sample.Method.Should().Be("GET");
        sample.Path.Should().Be("/hello");
        sample.StatusCode.Should().Be(201);
        sample.ClientIp.Should().Be("10.1.2.3");
        sample.Error.Should().BeNull();
        monitor.Snapshot(null).Single().Requests.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_WithCaptureEnabled_RecordsBodiesAndExactBytes()
    {
        var monitor = new TrafficMonitor();
        var settings = await CreateSettingsAsync(captureEnabled: true, captureSize: 4096);
        var middleware = new TrafficMonitorMiddleware(
            async ctx =>
            {
                // Writes go through the middleware-installed capture stream.
                await ctx.Response.WriteAsync("response-payload");
                ctx.Response.StatusCode = 200;
            },
            monitor);

        var context = CreateContext(settings);
        await middleware.InvokeAsync(context);

        var sample = monitor.RecentRequests(1).Should().ContainSingle().Subject;
        sample.ResponseBody.Should().Be("response-payload");
        sample.BytesOut.Should().Be("response-payload".Length);
        // Capture is bounded: a body larger than the cap is truncated.
        var monitor2 = new TrafficMonitor();
        var settings2 = await CreateSettingsAsync(captureEnabled: true, captureSize: 5);
        var middleware2 = new TrafficMonitorMiddleware(
            async ctx => await ctx.Response.WriteAsync("0123456789"),
            monitor2);
        var context2 = CreateContext(settings2);
        await middleware2.InvokeAsync(context2);

        var truncated = monitor2.RecentRequests(1).Should().ContainSingle().Subject;
        truncated.ResponseBody.Should().Be("01234");
        truncated.BytesOut.Should().Be(10);
    }

    [Fact]
    public async Task InvokeAsync_Exception_RecordsErrorAndRethrows()
    {
        var monitor = new TrafficMonitor();
        var settings = await CreateSettingsAsync(captureEnabled: false, captureSize: 0);
        var middleware = new TrafficMonitorMiddleware(
            _ => throw new InvalidOperationException("upstream exploded"),
            monitor);

        var context = CreateContext(settings);
        var ex = await Record.ExceptionAsync(() => middleware.InvokeAsync(context));
        ex.Should().BeOfType<InvalidOperationException>();

        var sample = monitor.RecentRequests(1).Should().ContainSingle().Subject;
        sample.Error.Should().Be("upstream exploded");
        monitor.Snapshot(null).Single().Failed.Should().Be(1);
        monitor.Snapshot(null).Single().Active.Should().Be(0); // decremented in finally
    }
}
