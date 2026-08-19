using FluentValidation;
using ProxyManager.Application.Exceptions;
using ProxyManager.Application.ProxyHosts;
using ProxyManager.Domain;

namespace ProxyManager.Application.Redirects;

/// <summary>Use-cases for redirection hosts, with cross-entity domain conflict checks.</summary>
public sealed class RedirectHostService
{
    private readonly IRedirectHostRepository _redirects;
    private readonly IProxyHostRepository _proxyHosts;
    private readonly IConfigReloadNotifier _notifier;
    private readonly RedirectHostValidator _validator;
    private readonly TimeProvider _time;

    public RedirectHostService(
        IRedirectHostRepository redirects,
        IProxyHostRepository proxyHosts,
        IConfigReloadNotifier notifier,
        RedirectHostValidator validator,
        TimeProvider? time = null)
    {
        _redirects = redirects;
        _proxyHosts = proxyHosts;
        _notifier = notifier;
        _validator = validator;
        _time = time ?? TimeProvider.System;
    }

    public Task<IReadOnlyList<RedirectHost>> ListAsync(CancellationToken cancellationToken = default)
        => _redirects.ListAsync(cancellationToken);

    public Task<RedirectHost?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _redirects.GetAsync(id, cancellationToken);

    public async Task<RedirectHost> CreateAsync(RedirectHostInput input, CancellationToken cancellationToken = default)
    {
        await ValidateAndCheckConflictsAsync(input, excludingId: null, cancellationToken);

        var now = _time.GetUtcNow();
        var redirect = new RedirectHost { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        ApplyInput(redirect, input);
        await _redirects.AddAsync(redirect, cancellationToken);
        _notifier.Notify();
        return redirect;
    }

    public async Task<RedirectHost> UpdateAsync(Guid id, RedirectHostInput input, CancellationToken cancellationToken = default)
    {
        var redirect = await _redirects.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Redirect host '{id}' was not found.");

        await ValidateAndCheckConflictsAsync(input, id, cancellationToken);

        ApplyInput(redirect, input);
        redirect.UpdatedAt = _time.GetUtcNow();
        await _redirects.UpdateAsync(redirect, cancellationToken);
        _notifier.Notify();
        return redirect;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var redirect = await _redirects.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Redirect host '{id}' was not found.");

        await _redirects.DeleteAsync(redirect, cancellationToken);
        _notifier.Notify();
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken = default)
    {
        var redirect = await _redirects.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Redirect host '{id}' was not found.");

        redirect.Enabled = enabled;
        redirect.UpdatedAt = _time.GetUtcNow();
        await _redirects.UpdateAsync(redirect, cancellationToken);
        _notifier.Notify();
    }

    private async Task ValidateAndCheckConflictsAsync(RedirectHostInput input, Guid? excludingId, CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(input, cancellationToken);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation.Errors);
        }

        var otherRedirects = await _redirects.ListAsync(cancellationToken);
        foreach (var existing in otherRedirects)
        {
            if (excludingId.HasValue && existing.Id == excludingId.Value)
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
                            $"Domain '{newDomain}' conflicts with '{existingDomain}' on redirect '{existing.Name}'.");
                    }
                }
            }
        }

        var proxyHosts = await _proxyHosts.ListAsync(cancellationToken);
        foreach (var existing in proxyHosts)
        {
            foreach (var existingDomain in existing.DomainNames)
            {
                foreach (var newDomain in input.DomainNames)
                {
                    if (DomainName.Overlaps(existingDomain, newDomain))
                    {
                        throw new DomainConflictException(
                            $"Domain '{newDomain}' conflicts with '{existingDomain}' on proxy host '{existing.Name}'.");
                    }
                }
            }
        }
    }

    private static void ApplyInput(RedirectHost redirect, RedirectHostInput input)
    {
        redirect.Name = input.Name.Trim();
        redirect.DomainNames = input.DomainNames.Select(DomainName.Normalize).ToList();
        redirect.Enabled = input.Enabled;
        redirect.StatusCode = input.StatusCode;
        redirect.PreservePath = input.PreservePath;
        redirect.ForwardScheme = input.ForwardScheme.ToLowerInvariant();
        redirect.ForwardHost = input.ForwardHost.Trim();
        redirect.ForwardPort = input.ForwardPort;
        redirect.CertificateId = input.CertificateId;
    }
}
