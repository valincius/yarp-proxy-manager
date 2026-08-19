namespace ProxyManager.Domain;

/// <summary>
/// A token used to authenticate REST API requests via the <c>X-Api-Key</c> header.
/// Only the salted SHA-256 hash is stored; the plaintext key is shown once at creation.
/// </summary>
public sealed class ApiKey
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Hex of the salt and hash, "<c>salt:hash</c>".</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>First characters of the plaintext key, for display in the UI.</summary>
    public string Prefix { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}
