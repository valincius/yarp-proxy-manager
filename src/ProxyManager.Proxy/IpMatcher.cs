using System.Net;

namespace ProxyManager.Proxy;

/// <summary>IP / CIDR / wildcard matching used by access lists.</summary>
public static class IpMatcher
{
    public static bool Matches(string pattern, IPAddress? remoteIp)
    {
        if (remoteIp is null)
        {
            return false;
        }

        if (pattern == "*")
        {
            return true;
        }

        var parts = pattern.Split('/');
        if (parts.Length == 1)
        {
            return IPAddress.TryParse(parts[0], out var ip) && ip.Equals(remoteIp);
        }

        if (parts.Length == 2
            && IPAddress.TryParse(parts[0], out var network)
            && int.TryParse(parts[1], out var prefix))
        {
            var networkBytes = network.GetAddressBytes();
            var remoteBytes = remoteIp.GetAddressBytes();
            if (networkBytes.Length != remoteBytes.Length)
            {
                return false;
            }

            var maxPrefix = networkBytes.Length * 8;
            if (prefix is < 0 or > 128)
            {
                return false;
            }

            var fullBytes = prefix / 8;
            var remainingBits = prefix % 8;
            for (var i = 0; i < fullBytes; i++)
            {
                if (networkBytes[i] != remoteBytes[i])
                {
                    return false;
                }
            }

            if (remainingBits > 0)
            {
                var mask = (byte)(0xFF << (8 - remainingBits));
                if ((networkBytes[fullBytes] & mask) != (remoteBytes[fullBytes] & mask))
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }
}
