using System.Net;
using ProxyManager.Application.Proxy;

namespace ProxyManager.Proxy;

/// <summary>
/// Evaluates NPM-style access-list semantics: a matching Deny rule always blocks;
/// otherwise SatisfyAny allows when any Allow rule matches, SatisfyAll allows only
/// when every rule matches. An empty list denies (fail closed).
/// </summary>
public static class AccessListPolicyEvaluator
{
    public static bool IsAllowed(AccessListPolicy policy, IPAddress? remoteIp)
    {
        if (policy.Rules.Count == 0)
        {
            return false;
        }

        var matchesAny = policy.Rules.Any(r => IpMatcher.Matches(r.Pattern, remoteIp));
        var matchesDeny = policy.Rules.Any(r => r.Action == "Deny" && IpMatcher.Matches(r.Pattern, remoteIp));

        if (matchesDeny)
        {
            return false;
        }

        return policy.SatisfyAny ? matchesAny : policy.Rules.All(r => IpMatcher.Matches(r.Pattern, remoteIp));
    }
}
