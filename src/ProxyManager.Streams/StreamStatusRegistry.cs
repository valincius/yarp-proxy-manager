using System.Collections.Concurrent;
using ProxyManager.Domain;

namespace ProxyManager.Streams;

/// <summary>Runtime status of a stream listener (consumed by the admin API/UI).</summary>
public sealed record StreamStatus(
    bool Listening,
    string? Error,
    int ActiveSessions,
    long BytesIn,
    long BytesOut,
    DateTimeOffset UpdatedAt);

public sealed class StreamStatusRegistry
{
    private readonly ConcurrentDictionary<Guid, StreamStatus> _statuses = new();

    public void Set(Guid streamId, StreamStatus status) => _statuses[streamId] = status;

    public bool TryGet(Guid streamId, out StreamStatus status) => _statuses.TryGetValue(streamId, out status!);

    public IReadOnlyDictionary<Guid, StreamStatus> Snapshot() => _statuses.ToDictionary(kv => kv.Key, kv => kv.Value);

    public void Remove(Guid streamId) => _statuses.TryRemove(streamId, out _);
}

/// <summary>A running forwarder for one stream entry.</summary>
public interface IStreamListener : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);

    bool Matches(Domain.Stream stream);
}
