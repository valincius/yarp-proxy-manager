using FluentValidation;
using ProxyManager.Application.Exceptions;
using ProxyManager.Domain;

namespace ProxyManager.Application.Redirects;

/// <summary>Use-cases for access lists (allow/deny rules attached to proxy hosts).</summary>
public sealed class AccessListService
{
    private readonly IAccessListRepository _repository;
    private readonly IConfigReloadNotifier _notifier;
    private readonly AccessListValidator _validator;
    private readonly TimeProvider _time;

    public AccessListService(
        IAccessListRepository repository,
        IConfigReloadNotifier notifier,
        AccessListValidator validator,
        TimeProvider? time = null)
    {
        _repository = repository;
        _notifier = notifier;
        _validator = validator;
        _time = time ?? TimeProvider.System;
    }

    public Task<IReadOnlyList<AccessList>> ListAsync(CancellationToken cancellationToken = default)
        => _repository.ListAsync(cancellationToken);

    public Task<AccessList?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _repository.GetAsync(id, cancellationToken);

    public async Task<AccessList> CreateAsync(AccessListInput input, CancellationToken cancellationToken = default)
    {
        await _validator.ValidateAndThrowAsync(input, cancellationToken);

        var now = _time.GetUtcNow();
        var accessList = new AccessList { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        ApplyInput(accessList, input);
        await _repository.AddAsync(accessList, cancellationToken);
        _notifier.Notify();
        return accessList;
    }

    public async Task<AccessList> UpdateAsync(Guid id, AccessListInput input, CancellationToken cancellationToken = default)
    {
        var accessList = await _repository.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Access list '{id}' was not found.");

        await _validator.ValidateAndThrowAsync(input, cancellationToken);

        ApplyInput(accessList, input);
        accessList.UpdatedAt = _time.GetUtcNow();
        await _repository.UpdateAsync(accessList, cancellationToken);
        _notifier.Notify();
        return accessList;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var accessList = await _repository.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Access list '{id}' was not found.");

        await _repository.DeleteAsync(accessList, cancellationToken);
        _notifier.Notify();
    }

    private static void ApplyInput(AccessList accessList, AccessListInput input)
    {
        accessList.Name = input.Name.Trim();
        accessList.SatisfyAny = input.SatisfyAny;
        accessList.Rules = input.Rules.Select(r => new AccessListRule
        {
            Id = Guid.NewGuid(),
            AccessListId = accessList.Id,
            Action = r.Action,
            Pattern = r.Pattern.Trim(),
        }).ToList();
    }
}
