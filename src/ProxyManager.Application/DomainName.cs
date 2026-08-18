using System.Text.RegularExpressions;

namespace ProxyManager.Application;

/// <summary>Helpers for proxy host domain names (hostnames, wildcards, IPv4).</summary>
public static partial class DomainName
{
    [GeneratedRegex(@"^(?:\*\.)?(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)*[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HostRegex();

    [GeneratedRegex(@"^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$", RegexOptions.Compiled)]
    private static partial Regex IpV4Regex();

    public static bool IsValid(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain) || domain.Length > 253)
        {
            return false;
        }

        var value = domain.Trim().ToLowerInvariant();
        if (HostRegex().IsMatch(value))
        {
            return true;
        }

        var match = IpV4Regex().Match(value);
        if (!match.Success)
        {
            return false;
        }

        for (var i = 1; i <= 4; i++)
        {
            if (int.Parse(match.Groups[i].Value) > 255)
            {
                return false;
            }
        }

        return true;
    }

    public static string Normalize(string domain) => domain.Trim().ToLowerInvariant();

    /// <summary>
    /// Returns true when two domains target the same hostnames (equal, or one is a wildcard covering the other).
    /// </summary>
    public static bool Overlaps(string a, string b)
    {
        a = Normalize(a);
        b = Normalize(b);
        if (a == b)
        {
            return true;
        }

        if (a.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = a[1..]; // ".example.com"
            if (b.EndsWith(suffix, StringComparison.Ordinal) || b == a[2..])
            {
                return true;
            }
        }

        if (b.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = b[1..];
            if (a.EndsWith(suffix, StringComparison.Ordinal) || a == b[2..])
            {
                return true;
            }
        }

        return false;
    }
}
