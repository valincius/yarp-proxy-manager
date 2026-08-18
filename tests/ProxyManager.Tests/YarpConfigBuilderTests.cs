using FluentAssertions;
using ProxyManager.Application.ProxyHosts;
using ProxyManager.Proxy;
using Xunit;

namespace ProxyManager.Tests;

public sealed class YarpConfigBuilderTests
{
    private static HostConfig Host(
        Guid id,
        string[] domains,
        IReadOnlyList<LocationConfig>? locations = null,
        IReadOnlyList<HeaderConfig>? requestHeaders = null,
        IReadOnlyList<HeaderConfig>? responseHeaders = null) =>
        new(id, domains, "http", "10.0.0.5", 3000, true, true, false, true,
            locations ?? [], requestHeaders ?? [], responseHeaders ?? []);

    [Fact]
    public void SimpleHost_ProducesOneRouteAndOneCluster()
    {
        var host = Host(Guid.NewGuid(), ["app.example.com"]);

        var (routes, clusters) = YarpConfigBuilder.Build([host]);

        routes.Should().HaveCount(1);
        clusters.Should().HaveCount(1);

        var route = routes[0];
        var cluster = clusters[0];

        route.ClusterId.Should().Be(cluster.ClusterId);
        route.Match.Hosts.Should().Equal("app.example.com");
        route.Match.Path.Should().Be("{**catch-all}");
        cluster.Destinations.Should().ContainKey("default");
        cluster.Destinations["default"]!.Address.Should().Be("http://10.0.0.5:3000/");
    }

    [Fact]
    public void HostWithLocations_ProducesExtraRoutesAndClusters()
    {
        var host = Host(
            Guid.NewGuid(),
            ["example.com"],
            locations:
            [
                new LocationConfig("/api", StripPrefix: true, "http", "10.0.0.6", 5000),
            ]);

        var (routes, clusters) = YarpConfigBuilder.Build([host]);

        // Two location routes (bare prefix + catch-all) plus the host catch-all.
        routes.Should().HaveCount(3);
        clusters.Should().HaveCount(2);

        var locationRoutes = routes.Where(r => r.RouteId.Contains("loc-", StringComparison.Ordinal)).ToList();
        locationRoutes.Should().HaveCount(2);

        // Strip transform present on location routes.
        locationRoutes.Should().OnlyContain(r => r.Transforms != null &&
            r.Transforms.Any(t => HasTransform(t, "PathRemovePrefix", "/api")));

        // Default catch-all outranks location routes.
        var defaultRoute = routes.Single(r => r.Match.Path == "{**catch-all}");
        defaultRoute.Order.Should().Be(100);
    }

    [Fact]
    public void Headers_ProduceRequestAndResponseTransforms()
    {
        var host = Host(
            Guid.NewGuid(),
            ["app.example.com"],
            requestHeaders: [new HeaderConfig("Request", "Set", "X-Custom", "hello")],
            responseHeaders: [new HeaderConfig("Response", "Remove", "X-Secret", "")]);

        var (routes, _) = YarpConfigBuilder.Build([host]);

        var transforms = routes.Single().Transforms!;
        transforms.Should().Contain(t =>
            HasTransform(t, "RequestHeader", "X-Custom") && HasTransform(t, "Set", "hello"));
        transforms.Should().Contain(t =>
            HasTransform(t, "ResponseHeader", "X-Secret") && t.ContainsKey("Remove"));
    }

    private static bool HasTransform(IReadOnlyDictionary<string, string> transform, string key, string? expectedValue = null)
    {
        if (!transform.TryGetValue(key, out var value))
        {
            return false;
        }

        return expectedValue is null || value == expectedValue;
    }

    [Fact]
    public void MultipleDomains_AreAllMatched()
    {
        var host = Host(Guid.NewGuid(), ["a.example.com", "b.example.com", "*.example.com"]);

        var (routes, _) = YarpConfigBuilder.Build([host]);

        routes.Single().Match.Hosts.Should().BeEquivalentTo("a.example.com", "b.example.com", "*.example.com");
    }
}
