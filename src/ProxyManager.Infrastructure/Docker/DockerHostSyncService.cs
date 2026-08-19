using global::Docker.DotNet.Models;
using FluentValidation;
using Microsoft.Extensions.Logging;
using ProxyManager.Application.Exceptions;
using ProxyManager.Application.ProxyHosts;
using ProxyManager.Application.Settings;
using ProxyManager.Domain;

using DockerClient = global::Docker.DotNet.IDockerClient;

namespace ProxyManager.Infrastructure.Docker;

/// <summary>
/// Traefik-style autodiscovery: containers labelled <c>proxy-manager.enable=true</c> are
/// published as proxy hosts (domain + container IP + port), and the hosts are disposed
/// again when the container disappears or drops the labels. Runs on a timer; see
/// DockerSyncWorker in the API project.
/// </summary>
public sealed class DockerHostSyncService(
    ProxyHostService hosts,
    SettingsService settings,
    DockerClientFactory clientFactory,
    ILogger<DockerHostSyncService> logger)
{
    private const string LabelEnable = "proxy-manager.enable";
    private const string LabelHost = "proxy-manager.host";
    private const string LabelPort = "proxy-manager.port";
    private const string LabelScheme = "proxy-manager.scheme";
    private const string LabelName = "proxy-manager.name";
    private const string ManagedByDocker = "docker";

    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        var enabled = await settings.GetAsync("Docker:Enabled", cancellationToken) == "true";
        if (!enabled)
        {
            return;
        }

        var networkName = await settings.GetAsync("Docker:Network", cancellationToken);
        var hostOverride = await settings.GetAsync("Docker:Host", cancellationToken);
        var discovered = 0;
        var managedCount = 0;
        var errors = new List<string>();
        var desiredSources = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            using var client = clientFactory.CreateClient(hostOverride);
            var containers = await client.Containers.ListContainersAsync(
                new ContainersListParameters { All = false }, cancellationToken);

            foreach (var container in containers)
            {
                var labels = container.Labels;
                if (labels is null || !labels.TryGetValue(LabelEnable, out var enable) || enable != "true")
                {
                    continue;
                }

                try
                {
                    await PublishContainerAsync(client, container, labels, networkName, desiredSources, cancellationToken);
                    discovered++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add($"container {ShortId(container.ID)}: {ex.Message}");
                    logger.LogWarning(ex, "Docker autodiscovery: failed to publish container {Id}", ShortId(container.ID));
                }
            }

            // Dispose hosts whose container is gone / no longer published.
            var managed = await hosts.ListManagedAsync(ManagedByDocker, cancellationToken);
            foreach (var host in managed)
            {
                if (host.ManagedSource is not null && !desiredSources.Contains(host.ManagedSource))
                {
                    logger.LogInformation("Docker autodiscovery: removing host '{Name}' (container {Source} gone)", host.Name, host.ManagedSource);
                    await hosts.DeleteAsync(host.Id, cancellationToken);
                }
                else
                {
                    managedCount++;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add($"docker engine: {ex.Message}");
            logger.LogError(ex, "Docker autodiscovery sync failed");
        }

        await settings.SetDockerStatusAsync(
            DateTimeOffset.UtcNow,
            errors.Count > 0 ? string.Join("; ", errors) : null,
            managedCount,
            discovered,
            cancellationToken);
    }

    private async Task PublishContainerAsync(
        DockerClient client,
        ContainerListResponse container,
        IDictionary<string, string> labels,
        string? networkName,
        ISet<string> desiredSources,
        CancellationToken cancellationToken)
    {
        var domains = (labels.TryGetValue(LabelHost, out var hostLabel) ? hostLabel : string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (domains.Count == 0)
        {
            throw new InvalidOperationException("label 'proxy-manager.host' is required");
        }

        if (!labels.TryGetValue(LabelPort, out var portLabel) || !int.TryParse(portLabel, out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException("label 'proxy-manager.port' must be a valid port (1-65535)");
        }

        var ip = ResolveIp(container, networkName);
        if (string.IsNullOrEmpty(ip))
        {
            throw new InvalidOperationException($"no IP address in network '{networkName ?? "(any)"}'");
        }

        var scheme = labels.TryGetValue(LabelScheme, out var schemeLabel) && schemeLabel == "https"
            ? "https"
            : "http";
        var name = labels.TryGetValue(LabelName, out var nameLabel) && !string.IsNullOrWhiteSpace(nameLabel)
            ? nameLabel.Trim()
            : container.Names.FirstOrDefault()?.TrimStart('/') ?? container.ID[..12];

        var source = "container:" + container.ID;
        desiredSources.Add(source);

        var input = new ProxyHostInput(
            Name: name,
            DomainNames: domains,
            Enabled: true,
            Scheme: scheme,
            ForwardHost: ip,
            ForwardPort: port,
            WebSocketsEnabled: true,
            BlockCommonExploits: true,
            ForceHttps: false,
            Http2Support: true,
            CertificateId: null,
            AccessListId: null,
            RequestHeaders: [],
            ResponseHeaders: [],
            Locations: [],
            Destinations: [],
            LoadBalancingPolicy: null,
            HealthCheckEnabled: false,
            HealthCheckPath: null,
            HealthCheckIntervalSeconds: 10);

        var existing = await hosts.FindByManagedSourceAsync(source, cancellationToken);
        if (existing is null)
        {
            try
            {
                await hosts.CreateManagedAsync(input, ManagedByDocker, source, cancellationToken);
                logger.LogInformation("Docker autodiscovery: published host '{Name}' ({Domains} → {Ip}:{Port})",
                    name, string.Join(", ", domains), ip, port);
            }
            catch (DomainConflictException ex)
            {
                // A manual host already owns this domain — leave it alone.
                logger.LogWarning(ex, "Docker autodiscovery: skipping container {Id} due to a domain conflict", ShortId(container.ID));
                throw;
            }
        }
        else if (existing.ForwardHost != ip || existing.ForwardPort != port || existing.Scheme != scheme)
        {
            await hosts.UpdateManagedAsync(existing.Id, input, cancellationToken);
            logger.LogInformation("Docker autodiscovery: updated host '{Name}' → {Ip}:{Port}", existing.Name, ip, port);
        }
    }

    private static string? ResolveIp(ContainerListResponse container, string? networkName)
    {
        if (container.NetworkSettings?.Networks is not { Count: > 0 } networks)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(networkName)
            && networks.TryGetValue(networkName, out var selected))
        {
            return string.IsNullOrEmpty(selected.IPAddress) ? null : selected.IPAddress;
        }

        // No network configured: prefer the first network with an address.
        foreach (var network in networks.Values)
        {
            if (!string.IsNullOrEmpty(network.IPAddress))
            {
                return network.IPAddress;
            }
        }

        return null;
    }

    private static string ShortId(string? id) => id is { Length: > 12 } ? id[..12] : id ?? "?";
}
