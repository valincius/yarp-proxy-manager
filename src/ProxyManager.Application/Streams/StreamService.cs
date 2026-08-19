using FluentValidation;
using ProxyManager.Application.Exceptions;
using ProxyManager.Domain;

namespace ProxyManager.Application.Streams;

public sealed class StreamValidator : AbstractValidator<StreamInput>
{
    public StreamValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Protocol).Must(static p => p is "Tcp" or "Udp").WithMessage("Protocol must be 'Tcp' or 'Udp'.");
        RuleFor(x => x.ListenPort).InclusiveBetween(1, 65535);
        RuleFor(x => x.ForwardHost).NotEmpty().MaximumLength(253);
        RuleFor(x => x.ForwardPort).InclusiveBetween(1, 65535);
    }
}

public sealed class StreamService
{
    private readonly IStreamRepository _repository;
    private readonly IReservedPortsProvider _reservedPorts;
    private readonly IConfigReloadNotifier _notifier;
    private readonly StreamValidator _validator;
    private readonly TimeProvider _time;

    public StreamService(
        IStreamRepository repository,
        IReservedPortsProvider reservedPorts,
        IConfigReloadNotifier notifier,
        StreamValidator validator,
        TimeProvider? time = null)
    {
        _repository = repository;
        _reservedPorts = reservedPorts;
        _notifier = notifier;
        _validator = validator;
        _time = time ?? TimeProvider.System;
    }

    public Task<IReadOnlyList<Domain.Stream>> ListAsync(CancellationToken cancellationToken = default)
        => _repository.ListAsync(cancellationToken);

    public Task<Domain.Stream?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _repository.GetAsync(id, cancellationToken);

    public async Task<Domain.Stream> CreateAsync(StreamInput input, CancellationToken cancellationToken = default)
    {
        await ValidateAndCheckPortAsync(input, excludingId: null, cancellationToken);

        var now = _time.GetUtcNow();
        var stream = new Domain.Stream { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        ApplyInput(stream, input);
        await _repository.AddAsync(stream, cancellationToken);
        _notifier.Notify();
        return stream;
    }

    public async Task<Domain.Stream> UpdateAsync(Guid id, StreamInput input, CancellationToken cancellationToken = default)
    {
        var stream = await _repository.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Stream '{id}' was not found.");

        await ValidateAndCheckPortAsync(input, id, cancellationToken);

        ApplyInput(stream, input);
        stream.UpdatedAt = _time.GetUtcNow();
        await _repository.UpdateAsync(stream, cancellationToken);
        _notifier.Notify();
        return stream;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var stream = await _repository.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Stream '{id}' was not found.");

        await _repository.DeleteAsync(stream, cancellationToken);
        _notifier.Notify();
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken = default)
    {
        var stream = await _repository.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Stream '{id}' was not found.");

        stream.Enabled = enabled;
        stream.UpdatedAt = _time.GetUtcNow();
        await _repository.UpdateAsync(stream, cancellationToken);
        _notifier.Notify();
    }

    private async Task ValidateAndCheckPortAsync(StreamInput input, Guid? excludingId, CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(input, cancellationToken);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation.Errors);
        }

        if (_reservedPorts.Ports.Contains(input.ListenPort))
        {
            throw new DomainConflictException(
                $"Listen port {input.ListenPort} is reserved by the proxy (HTTP/HTTPS/admin endpoints).");
        }

        var streams = await _repository.ListAsync(cancellationToken);
        foreach (var existing in streams)
        {
            if (excludingId.HasValue && existing.Id == excludingId.Value)
            {
                continue;
            }

            if (existing.ListenPort == input.ListenPort)
            {
                throw new DomainConflictException(
                    $"Listen port {input.ListenPort} is already used by stream '{existing.Name}'.");
            }
        }
    }

    private static void ApplyInput(Domain.Stream stream, StreamInput input)
    {
        stream.Name = input.Name.Trim();
        stream.Enabled = input.Enabled;
        stream.Protocol = input.Protocol == "Udp" ? StreamProtocol.Udp : StreamProtocol.Tcp;
        stream.ListenPort = input.ListenPort;
        stream.ForwardHost = input.ForwardHost.Trim();
        stream.ForwardPort = input.ForwardPort;
    }
}
