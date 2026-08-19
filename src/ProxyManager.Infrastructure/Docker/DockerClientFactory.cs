using Docker.DotNet;

namespace ProxyManager.Infrastructure.Docker;

/// <summary>
/// Creates a Docker API client. The engine endpoint can be configured in
/// appsettings (<c>Docker:Host</c>) or per-call from the settings store; when
/// neither is set, Docker.DotNet's defaults are used (named pipe on Windows,
/// unix socket on Linux).
/// </summary>
public sealed class DockerClientFactory(string? configuredHost)
{
    public string? ConfiguredHost { get; } = configuredHost;

    public IDockerClient CreateClient(string? hostOverride = null)
    {
        var host = hostOverride ?? ConfiguredHost;
        var configuration = string.IsNullOrWhiteSpace(host)
            ? new DockerClientConfiguration()
            : new DockerClientConfiguration(new Uri(host));
        return configuration.CreateClient();
    }
}
