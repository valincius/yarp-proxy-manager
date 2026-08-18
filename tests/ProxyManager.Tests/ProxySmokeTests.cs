using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Yarp.ReverseProxy.Configuration;

namespace ProxyManager.Tests;

/// <summary>
/// Phase 0 smoke test: proves YARP 2.3.0 (net8.0 assembly) runs on the net10.0
/// runtime and proxies HTTP traffic between two real Kestrel servers.
/// </summary>
public sealed class ProxySmokeTests
{
    [Fact]
    public async Task YarpOnNet10_ProxiesHttpRequest_ToUpstream()
    {
        // 1. Upstream server on an ephemeral port.
        var upstreamBuilder = WebApplication.CreateBuilder();
        upstreamBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var upstream = upstreamBuilder.Build();
        upstream.MapGet("/", () => Results.Text("hello-from-upstream"));
        await upstream.StartAsync();
        var upstreamAddress = GetServerAddress(upstream);

        // 2. Proxy server on an ephemeral port, configured from in-memory YARP config.
        var proxyBuilder = WebApplication.CreateBuilder();
        proxyBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        proxyBuilder.Services.AddReverseProxy().LoadFromMemory(
            new[]
            {
                new RouteConfig
                {
                    RouteId = "r1",
                    ClusterId = "c1",
                    Match = new RouteMatch { Path = "{**catch-all}" },
                },
            },
            new[]
            {
                new ClusterConfig
                {
                    ClusterId = "c1",
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        ["d1"] = new() { Address = upstreamAddress },
                    },
                },
            });
        await using var proxy = proxyBuilder.Build();
        proxy.MapReverseProxy();
        await proxy.StartAsync();
        var proxyAddress = GetServerAddress(proxy);

        // 3. Assert the full path works: client -> YARP -> upstream.
        using var client = new HttpClient();
        var body = await client.GetStringAsync(proxyAddress + "/");

        Assert.Equal("hello-from-upstream", body);

        await proxy.StopAsync();
        await upstream.StopAsync();
    }

    private static string GetServerAddress(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var feature = server.Features.Get<IServerAddressesFeature>();
        Assert.NotNull(feature);
        return Assert.Single(feature.Addresses);
    }
}
