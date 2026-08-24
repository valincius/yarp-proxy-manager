using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ProxyManager.Application.ProxyHosts;
using ProxyManager.Application.Redirects;
using ProxyManager.Certificates;
using ProxyManager.Domain;
using Xunit;

namespace ProxyManager.Tests;

public sealed class CertificateDeduplicatorTests
{
    private readonly InMemoryCertificateRepository _certificates = new();
    private readonly InMemoryHostRepository _hosts = new();
    private readonly InMemoryRedirectHostRepository _redirects = new();
    private readonly CertificateDeduplicator _deduplicator;

    public CertificateDeduplicatorTests()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "dedup-tests-" + Guid.NewGuid().ToString("N"));
        _deduplicator = new CertificateDeduplicator(
            _certificates,
            _hosts,
            _redirects,
            new CertificateFileStore(dataDirectory),
            new NoopReloadNotifier(),
            NullLogger<CertificateDeduplicator>.Instance);
    }

    private static Certificate IssuedCert(string name, string[] domains, DateTimeOffset? createdAt = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Domains = domains.ToList(),
        Provider = CertificateProvider.Acme,
        Status = CertificateStatus.Issued,
        NotBefore = DateTimeOffset.UtcNow.AddDays(-1),
        NotAfter = DateTimeOffset.UtcNow.AddDays(60),
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task DeduplicateAsync_SameDomainSet_DeletesDuplicateAndRepointsHostAndRedirect()
    {
        var survivor = IssuedCert("survivor", ["app.example.com"]);
        var duplicate = IssuedCert("duplicate", ["app.example.com"]);
        _certificates.Certificates.AddRange([survivor, duplicate]);
        var host = new ProxyHost
        {
            Id = Guid.NewGuid(),
            Name = "App",
            DomainNames = ["app.example.com"],
            CertificateId = duplicate.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var redirect = new RedirectHost
        {
            Id = Guid.NewGuid(),
            Name = "App redirect",
            DomainNames = ["www.app.example.com"],
            CertificateId = duplicate.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _hosts.Hosts.Add(host);
        _redirects.Redirects.Add(redirect);

        await _deduplicator.DeduplicateAsync(survivor, CancellationToken.None);

        _certificates.Certificates.Should().ContainSingle().Which.Id.Should().Be(survivor.Id);
        _hosts.Hosts.Should().ContainSingle().Which.CertificateId.Should().Be(survivor.Id);
        _redirects.Redirects.Should().ContainSingle().Which.CertificateId.Should().Be(survivor.Id);
    }

    [Fact]
    public async Task DeduplicateAsync_DomainOrderAndCase_IsTreatedAsSameSet()
    {
        var survivor = IssuedCert("survivor", ["app.example.com", "api.example.com"]);
        var duplicate = IssuedCert("duplicate", ["API.EXAMPLE.COM", "app.example.com"]);
        _certificates.Certificates.AddRange([survivor, duplicate]);

        await _deduplicator.DeduplicateAsync(survivor, CancellationToken.None);

        _certificates.Certificates.Should().ContainSingle().Which.Id.Should().Be(survivor.Id);
    }

    [Fact]
    public async Task DeduplicateAsync_DifferentDomainSets_AreKept()
    {
        var baseCert = IssuedCert("base", ["example.com"]);
        var wildcard = IssuedCert("wild", ["*.example.com"]);
        var other = IssuedCert("other", ["other.example.com"]);
        _certificates.Certificates.AddRange([baseCert, wildcard, other]);

        await _deduplicator.DeduplicateAsync(baseCert, CancellationToken.None);

        _certificates.Certificates.Should().HaveCount(3);
    }

    [Fact]
    public async Task SweepAsync_KeepsBestPerDomainSetAndRepoints()
    {
        var older = IssuedCert("older", ["app.example.com"], DateTimeOffset.UtcNow.AddDays(-10));
        var newer = IssuedCert("newer", ["app.example.com"], DateTimeOffset.UtcNow.AddDays(-1));
        var failed = new Certificate
        {
            Id = Guid.NewGuid(),
            Name = "failed",
            Domains = ["app.example.com"],
            Provider = CertificateProvider.Acme,
            Status = CertificateStatus.Failed,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var unrelated = IssuedCert("unrelated", ["other.example.com"]);
        _certificates.Certificates.AddRange([older, newer, failed, unrelated]);
        var host = new ProxyHost
        {
            Id = Guid.NewGuid(),
            Name = "App",
            DomainNames = ["app.example.com"],
            CertificateId = failed.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _hosts.Hosts.Add(host);

        await _deduplicator.SweepAsync(CancellationToken.None);

        _certificates.Certificates.Should().HaveCount(2);
        var survivor = _certificates.Certificates.Single(c => c.Domains.Contains("app.example.com"));
        survivor.Id.Should().Be(newer.Id);
        _hosts.Hosts.Should().ContainSingle().Which.CertificateId.Should().Be(newer.Id);
    }

    [Fact]
    public async Task SweepAsync_NoDuplicates_IsNoOp()
    {
        _certificates.Certificates.AddRange(
        [
            IssuedCert("a", ["a.example.com"]),
            IssuedCert("b", ["b.example.com"]),
        ]);

        await _deduplicator.SweepAsync(CancellationToken.None);

        _certificates.Certificates.Should().HaveCount(2);
    }
}
