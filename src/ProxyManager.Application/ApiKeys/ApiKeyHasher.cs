using System.Security.Cryptography;
using System.Text;

namespace ProxyManager.Application.ApiKeys;

/// <summary>
/// Generates API keys and computes/verifies their salted SHA-256 hashes.
/// Key format: <c>yarp_&lt;43 base64url chars&gt;</c> (~256 bits of entropy).
/// Stored hash format: <c>&lt;saltHex&gt;:&lt;hashHex&gt;</c>.
/// </summary>
public static class ApiKeyHasher
{
    private const string Prefix = "yarp_";

    public static string Generate()
        => Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    public static string Hash(string plaintext, byte[] salt)
    {
        var saltHex = Convert.ToHexString(salt).ToLowerInvariant();
        var input = Encoding.UTF8.GetBytes(saltHex + plaintext);
        var hash = SHA256.HashData(input);
        return saltHex + ":" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Returns true when <paramref name="plaintext"/> matches the stored hash.</summary>
    public static bool Verify(string plaintext, string stored)
    {
        var separator = stored.IndexOf(':');
        if (separator <= 0)
        {
            return false;
        }

        var saltHex = stored[..separator];
        byte[] salt;
        try
        {
            salt = Convert.FromHexString(saltHex);
        }
        catch (FormatException)
        {
            return false;
        }

        return Hash(plaintext, salt) == stored;
    }
}
