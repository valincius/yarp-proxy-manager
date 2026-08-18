using System.Collections.Concurrent;

namespace ProxyManager.Certificates;

/// <summary>In-memory store of pending HTTP-01 challenge tokens (token → key authorization).</summary>
public sealed class Http01ChallengeStore
{
    private readonly ConcurrentDictionary<string, (string KeyAuthorization, DateTimeOffset CreatedAt)> _challenges = new();

    public void Set(string token, string keyAuthorization)
        => _challenges[token] = (keyAuthorization, DateTimeOffset.UtcNow);

    public void Remove(string token)
        => _challenges.TryRemove(token, out _);

    public bool TryGetValue(string token, out string keyAuthorization)
    {
        if (_challenges.TryGetValue(token, out var entry))
        {
            keyAuthorization = entry.KeyAuthorization;
            return true;
        }

        keyAuthorization = string.Empty;
        return false;
    }

    /// <summary>Removes entries older than the given age (challenges are valid for a short window).</summary>
    public void Cleanup(TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        foreach (var (token, entry) in _challenges)
        {
            if (entry.CreatedAt < cutoff)
            {
                _challenges.TryRemove(token, out _);
            }
        }
    }
}
