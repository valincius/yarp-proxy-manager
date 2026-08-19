using ProxyManager.Domain;

namespace ProxyManager.Application.Streams;

public sealed record StreamInput(
    string Name,
    bool Enabled,
    string Protocol,
    int ListenPort,
    string ForwardHost,
    int ForwardPort);

public interface IStreamRepository
{
    Task<IReadOnlyList<Domain.Stream>> ListAsync(CancellationToken cancellationToken = default);

    Task<Domain.Stream?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(Domain.Stream stream, CancellationToken cancellationToken = default);

    Task UpdateAsync(Domain.Stream stream, CancellationToken cancellationToken = default);

    Task DeleteAsync(Domain.Stream stream, CancellationToken cancellationToken = default);
}

/// <summary>Ports the proxy itself listens on; streams must not collide with them.</summary>
public interface IReservedPortsProvider
{
    IReadOnlyList<int> Ports { get; }
}
