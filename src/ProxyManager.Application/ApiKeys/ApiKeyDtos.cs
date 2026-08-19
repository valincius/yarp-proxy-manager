namespace ProxyManager.Application.ApiKeys;

/// <summary>Public view of an API key — never contains the hash or plaintext.</summary>
public sealed record ApiKeyDto(
    Guid Id,
    string Name,
    string Prefix,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt);

public sealed record CreateApiKeyRequest(string Name);

/// <summary>Result of creating a key: the stored record plus the one-time plaintext.</summary>
public sealed record CreatedApiKeyDto(ApiKeyDto Key, string Plaintext);
