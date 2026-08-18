using ProxyManager.Application.ProxyHosts;
using Yarp.ReverseProxy.Configuration;

namespace ProxyManager.Proxy;

/// <summary>
/// Compiles the manager's host model into YARP RouteConfig/ClusterConfig.
/// One host = one cluster (plus one cluster per custom location), with the host's
/// domains matched on the Host header and paths on a catch-all.
/// </summary>
public static class YarpConfigBuilder
{
    public static (IReadOnlyList<RouteConfig> Routes, IReadOnlyList<ClusterConfig> Clusters) Build(
        IEnumerable<HostConfig> hosts)
    {
        var routes = new List<RouteConfig>();
        var clusters = new List<ClusterConfig>();

        foreach (var host in hosts)
        {
            var clusterId = $"host-{host.Id:n}";
            clusters.Add(BuildCluster(clusterId, host.Scheme, host.ForwardHost, host.ForwardPort));

            var hostTransforms = BuildHeaderTransforms(host.RequestHeaders, host.ResponseHeaders);

            // Custom locations first: more specific path prefixes must outrank the catch-all.
            foreach (var location in host.Locations)
            {
                var prefix = NormalizePrefix(location.PathPrefix);
                var locationClusterId = $"{clusterId}-loc-{prefix.Trim('/').Replace('/', '-')}";
                clusters.Add(BuildCluster(locationClusterId, location.Scheme, location.ForwardHost, location.ForwardPort));

                var locationTransforms = new List<Dictionary<string, string>>();
                if (location.StripPrefix)
                {
                    locationTransforms.Add(new Dictionary<string, string> { ["PathRemovePrefix"] = prefix });
                }

                // Two routes per location: the bare prefix and everything below it.
                // The store returns locations ordered by their Order value, so list order
                // already expresses precedence; the default host route below is Order 100.
                routes.Add(new RouteConfig
                {
                    RouteId = $"{clusterId}-loc-{prefix.Trim('/').Replace('/', '-')}-root",
                    ClusterId = locationClusterId,
                    Match = new RouteMatch
                    {
                        Hosts = host.Domains,
                        Path = prefix,
                    },
                    Transforms = locationTransforms,
                });

                routes.Add(new RouteConfig
                {
                    RouteId = $"{clusterId}-loc-{prefix.Trim('/').Replace('/', '-')}-catchall",
                    ClusterId = locationClusterId,
                    Match = new RouteMatch
                    {
                        Hosts = host.Domains,
                        Path = $"{prefix}/{{**catch-all}}",
                    },
                    Transforms = locationTransforms,
                });
            }

            routes.Add(new RouteConfig
            {
                RouteId = clusterId,
                ClusterId = clusterId,
                Order = 100,
                Match = new RouteMatch
                {
                    Hosts = host.Domains,
                    Path = "{**catch-all}",
                },
                Transforms = hostTransforms,
            });
        }

        return (routes, clusters);
    }

    private static ClusterConfig BuildCluster(string clusterId, string scheme, string host, int port) => new()
    {
        ClusterId = clusterId,
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["default"] = new() { Address = $"{scheme}://{host}:{port}/" },
        },
    };

    private static List<Dictionary<string, string>> BuildHeaderTransforms(
        IReadOnlyList<HeaderConfig> requestHeaders,
        IReadOnlyList<HeaderConfig> responseHeaders)
    {
        var transforms = new List<Dictionary<string, string>>();

        foreach (var header in requestHeaders.Concat(responseHeaders))
        {
            var targetKey = header.Target == "Response" ? "ResponseHeader" : "RequestHeader";
            transforms.Add(new Dictionary<string, string>
            {
                [targetKey] = header.Name,
                [header.Action] = header.Action == "Remove" ? string.Empty : header.Value,
            });
        }

        return transforms;
    }

    private static string NormalizePrefix(string pathPrefix)
    {
        var prefix = pathPrefix.TrimEnd('/');
        return prefix.Length == 0 ? "/" : prefix;
    }
}
