using FluentAssertions;
using ProxyManager.Application.ProxyHosts;
using Xunit;

namespace ProxyManager.Tests;

public sealed class ProxyHostValidatorTests
{
    private readonly ProxyHostValidator _validator = new();

    private static ProxyHostInput Valid() =>
        new("Test host", ["app.example.com"], true, "http", "10.0.0.1", 8080,
            true, false, null, null, [], [], [], [], null, false, null, 10);

    [Fact]
    public void ValidInput_Passes()
    {
        var result = _validator.Validate(Valid());
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://example.com")]
    [InlineData("example.com/path")]
    [InlineData("exa mple.com")]
    [InlineData("example..com")]
    [InlineData("-example.com")]
    public void InvalidDomains_Fail(string domain)
    {
        var input = Valid() with { DomainNames = [domain] };
        var result = _validator.Validate(input);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void DuplicateDomainsWithinHost_Fail()
    {
        var input = Valid() with { DomainNames = ["app.example.com", "APP.EXAMPLE.COM"] };
        var result = _validator.Validate(input);
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void InvalidPort_Fails(int port)
    {
        var input = Valid() with { ForwardPort = port };
        var result = _validator.Validate(input);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void HeaderInjection_Fails()
    {
        var input = Valid() with
        {
            RequestHeaders = [new ProxyHeaderInput("Request", "Set", "X-Test", "value\r\nInjected: yes")],
        };
        var result = _validator.Validate(input);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void LocationWithoutSlashPrefix_Fails()
    {
        var input = Valid() with
        {
            Locations = [new ProxyLocationInput("api", true, "http", "10.0.0.2", 5000, 0)],
        };
        var result = _validator.Validate(input);
        result.IsValid.Should().BeFalse();
    }
}
