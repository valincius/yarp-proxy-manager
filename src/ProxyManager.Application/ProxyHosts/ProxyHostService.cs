using FluentValidation;
using ProxyManager.Application.Exceptions;
using ProxyManager.Domain;

namespace ProxyManager.Application.ProxyHosts;

/// <summary>
/// Use-cases for proxy hosts: validates input, checks cross-host domain conflicts,
/// persists through the repository, and notifies the config reloader.
/// </summary>
public sealed class ProxyHostService
{
    private readonly IProxyHostRepository _repository;
    private readonly IConfigReloadNotifier _notifier;
    private readonly ProxyHostValidator _validator;
    private readonly TimeProvider _time;

    public ProxyHostService(
        IProxyHostRepository repository,
        IConfigReloadNotifier notifier,
        ProxyHostValidator validator,
        TimeProvider? time = null)
    {
        _repository = repository;
        _notifier = notifier;
        _validator = validator;
        _time = time ?? TimeProvider.System;
    }

    public Task<IReadOnlyList<ProxyHost>> ListAsync(CancellationToken cancellationToken = default)
        => _repository.ListAsync(cancellationToken);

    public Task<ProxyHost?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _repository.GetAsync(id, cancellationToken);

    public async Task<ProxyHost> CreateAsync(ProxyHostInput input, CancellationToken cancellationToken = default)
    {
        await ValidateAndCheckConflictsAsync(input, excludingHostId: null, cancellationToken);

        var now = _time.GetUtcNow();
        var host = new ProxyHost { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        ApplyInput(host, input);

        await _repository.AddAsync(host, cancellationToken);
        _notifier.Notify();
        return host;
    }

    /// <summary>Creates a host owned by an automated source (Docker label autodiscovery).</summary>
    public async Task<ProxyHost> CreateManagedAsync(
        ProxyHostInput input,
        string managedBy,
        string managedSource,
        CancellationToken cancellationToken = default)
    {
        await ValidateAndCheckConflictsAsync(input, excludingHostId: null, cancellationToken);

        var now = _time.GetUtcNow();
        var host = new ProxyHost
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
            ManagedBy = managedBy,
            ManagedSource = managedSource,
        };
        ApplyInput(host, input);

        await _repository.AddAsync(host, cancellationToken);
        _notifier.Notify();
        return host;
    }

    /// <summary>Updates a managed host; the managed-by/source markers are preserved by ApplyInput.</summary>
    public async Task<ProxyHost> UpdateManagedAsync(Guid id, ProxyHostInput input, CancellationToken cancellationToken = default)
        => await UpdateAsync(id, input, cancellationToken);

    public Task<ProxyHost?> FindByManagedSourceAsync(string source, CancellationToken cancellationToken = default)
        => _repository.FindByManagedSourceAsync(source, cancellationToken);

    public Task<IReadOnlyList<ProxyHost>> ListManagedAsync(string managedBy, CancellationToken cancellationToken = default)
        => _repository.ListManagedAsync(managedBy, cancellationToken);

    public async Task<ProxyHost> UpdateAsync(Guid id, ProxyHostInput input, CancellationToken cancellationToken = default)
    {
        var host = await _repository.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Proxy host '{id}' was not found.");

        await ValidateAndCheckConflictsAsync(input, excludingHostId: id, cancellationToken);

        ApplyInput(host, input);
        host.UpdatedAt = _time.GetUtcNow();

        await _repository.UpdateAsync(host, cancellationToken);
        _notifier.Notify();
        return host;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var host = await _repository.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Proxy host '{id}' was not found.");

        await _repository.DeleteAsync(host, cancellationToken);
        _notifier.Notify();
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken = default)
    {
        var host = await _repository.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Proxy host '{id}' was not found.");

        host.Enabled = enabled;
        host.UpdatedAt = _time.GetUtcNow();

        await _repository.UpdateAsync(host, cancellationToken);
        _notifier.Notify();
    }

    private async Task ValidateAndCheckConflictsAsync(ProxyHostInput input, Guid? excludingHostId, CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(input, cancellationToken);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation.Errors);
        }

        var allHosts = await _repository.ListAsync(cancellationToken);
        foreach (var existing in allHosts)
        {
            if (excludingHostId.HasValue && existing.Id == excludingHostId.Value)
            {
                continue;
            }

            foreach (var existingDomain in existing.DomainNames)
            {
                foreach (var newDomain in input.DomainNames)
                {
                    if (DomainName.Overlaps(existingDomain, newDomain))
                    {
                        throw new DomainConflictException(
                            $"Domain '{newDomain}' conflicts with '{existingDomain}' on host '{existing.Name}'.");
                    }
                }
            }
        }
    }

    private static void ApplyInput(ProxyHost host, ProxyHostInput input)
    {
        host.Name = input.Name.Trim();
        host.DomainNames = input.DomainNames.Select(DomainName.Normalize).ToList();
        host.Enabled = input.Enabled;
        host.Scheme = input.Scheme.ToLowerInvariant();
        host.ForwardHost = input.ForwardHost.Trim();
        host.ForwardPort = input.ForwardPort;
        host.WebSocketsEnabled = input.WebSocketsEnabled;
        host.BlockCommonExploits = input.BlockCommonExploits;
        host.ForceHttps = input.ForceHttps;
        host.Http2Support = input.Http2Support;
        host.CertificateId = input.CertificateId;
        host.AccessListId = input.AccessListId;

        host.RequestHeaders = input.RequestHeaders.Select(h => new ProxyHeader
        {
            Id = Guid.NewGuid(),
            ProxyHostId = host.Id,
            Target = h.Target,
            Action = h.Action,
            Name = h.Name.Trim(),
            Value = h.Value,
        }).ToList();

        host.ResponseHeaders = input.ResponseHeaders.Select(h => new ProxyHeader
        {
            Id = Guid.NewGuid(),
            ProxyHostId = host.Id,
            Target = h.Target,
            Action = h.Action,
            Name = h.Name.Trim(),
            Value = h.Value,
        }).ToList();

        host.Locations = input.Locations.Select(l => new ProxyLocation
        {
            Id = Guid.NewGuid(),
            ProxyHostId = host.Id,
            PathPrefix = l.PathPrefix.TrimEnd('/'),
            StripPrefix = l.StripPrefix,
            Scheme = l.Scheme.ToLowerInvariant(),
            ForwardHost = l.ForwardHost.Trim(),
            ForwardPort = l.ForwardPort,
            Order = l.Order,
        }).ToList();

        host.Destinations = input.Destinations.Select(d => new ProxyDestination
        {
            Id = Guid.NewGuid(),
            ProxyHostId = host.Id,
            ForwardHost = d.ForwardHost.Trim(),
            ForwardPort = d.ForwardPort,
        }).ToList();

        host.LoadBalancingPolicy = string.IsNullOrWhiteSpace(input.LoadBalancingPolicy)
            ? null
            : input.LoadBalancingPolicy;
        host.HealthCheckEnabled = input.HealthCheckEnabled;
        host.HealthCheckPath = string.IsNullOrWhiteSpace(input.HealthCheckPath)
            ? null
            : input.HealthCheckPath;
        host.HealthCheckIntervalSeconds = input.HealthCheckIntervalSeconds;
    }
}
