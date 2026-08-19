using System.Net;
using FluentAssertions;
using ProxyManager.Application.Proxy;
using ProxyManager.Application.Redirects;
using ProxyManager.Proxy;
using Xunit;

namespace ProxyManager.Tests;

public sealed class IpMatcherTests
{
    private static IPAddress Ip(string value) => IPAddress.Parse(value);

    [Fact]
    public void Wildcard_MatchesAnything() => IpMatcher.Matches("*", Ip("10.0.0.5")).Should().BeTrue();

    [Fact]
    public void ExactIp_MatchesOnlyThatAddress()
    {
        IpMatcher.Matches("192.168.1.10", Ip("192.168.1.10")).Should().BeTrue();
        IpMatcher.Matches("192.168.1.10", Ip("192.168.1.11")).Should().BeFalse();
    }

    [Theory]
    [InlineData("10.0.0.0/8", "10.1.2.3", true)]
    [InlineData("10.0.0.0/8", "11.0.0.1", false)]
    [InlineData("192.168.1.0/24", "192.168.1.200", true)]
    [InlineData("192.168.1.0/24", "192.168.2.1", false)]
    [InlineData("2001:db8::/32", "2001:db8::1", true)]
    [InlineData("2001:db8::/32", "2001:db9::1", false)]
    [InlineData("10.0.0.0/32", "10.0.0.0", true)]
    [InlineData("10.0.0.0/32", "10.0.0.1", false)]
    public void Cidr_MatchesWithinNetwork(string cidr, string address, bool expected)
        => IpMatcher.Matches(cidr, Ip(address)).Should().Be(expected);

    [Fact]
    public void NullRemoteIp_NeverMatches() => IpMatcher.Matches("*", null).Should().BeFalse();
}

public sealed class AccessListPolicyEvaluatorTests
{
    private static AccessListPolicy Policy(bool satisfyAny, params (string Action, string Pattern)[] rules) =>
        new(satisfyAny, rules.Select(r => new AccessRule(r.Action, r.Pattern)).ToList());

    private static bool Allowed(AccessListPolicy policy, string address) =>
        AccessListPolicyEvaluator.IsAllowed(policy, IPAddress.Parse(address));

    [Fact]
    public void DenyRule_AlwaysBlocks()
    {
        Allowed(Policy(true, ("Allow", "*"), ("Deny", "127.0.0.1")), "127.0.0.1").Should().BeFalse();
        Allowed(Policy(false, ("Allow", "*"), ("Deny", "127.0.0.1")), "127.0.0.1").Should().BeFalse();
    }

    [Fact]
    public void SatisfyAny_AllowsWhenAnyAllowMatches()
    {
        Allowed(Policy(true, ("Allow", "10.0.0.0/8"), ("Allow", "192.168.1.5")), "10.1.0.1").Should().BeTrue();
        Allowed(Policy(true, ("Allow", "10.0.0.0/8"), ("Allow", "192.168.1.5")), "172.16.0.1").Should().BeFalse();
    }

    [Fact]
    public void SatisfyAll_RequiresEveryRuleToMatch()
    {
        Allowed(Policy(false, ("Allow", "10.0.0.0/8"), ("Allow", "10.0.0.5")), "10.0.0.5").Should().BeTrue();
        Allowed(Policy(false, ("Allow", "10.0.0.0/8"), ("Allow", "10.0.0.5")), "10.0.0.6").Should().BeFalse();
    }

    [Fact]
    public void EmptyRules_FailClosed() => Allowed(Policy(true), "1.2.3.4").Should().BeFalse();
}

public sealed class ExploitPatternsTests
{
    [Theory]
    [InlineData("/../../etc/passwd")]
    [InlineData("/download/%00")]
    [InlineData("/.git/config")]
    [InlineData("/backup.sql")]
    [InlineData("/wp-config.php")]
    [InlineData("/?id=1%20union%20select")]
    [InlineData("/?cmd=system('ls')")]
    public void SuspiciousTargets_AreBlocked(string target)
        => ExploitPatterns.IsSuspicious(target).Should().BeTrue();

    [Theory]
    [InlineData("/")]
    [InlineData("/healthz")]
    [InlineData("/api/users/42")]
    [InlineData("/assets/app.js?v=123")]
    public void NormalTargets_AreAllowed(string target)
        => ExploitPatterns.IsSuspicious(target).Should().BeFalse();
}

public sealed class RedirectValidatorsTests
{
    private readonly RedirectHostValidator _redirects = new();
    private readonly AccessListValidator _accessLists = new();

    [Fact]
    public void Redirect_ValidInput_Passes()
    {
        var result = _redirects.Validate(new RedirectHostInput(
            "Redirect", ["old.test"], true, 301, true, "http", "example.com", 80, null));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Redirect_InvalidStatusCode_Fails()
    {
        var result = _redirects.Validate(new RedirectHostInput(
            "Redirect", ["old.test"], true, 302, true, "http", "example.com", 80, null) with { StatusCode = 303 });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void AccessList_InvalidCidr_Fails()
    {
        var result = _accessLists.Validate(new AccessListInput(
            "List", true, [new AccessListRuleInput("Allow", "10.0.0.0/99")]));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void AccessList_ValidCidr_Passes()
    {
        var result = _accessLists.Validate(new AccessListInput(
            "List", true, [new AccessListRuleInput("Allow", "10.0.0.0/8"), new AccessListRuleInput("Deny", "*")]));
        result.IsValid.Should().BeTrue();
    }
}
