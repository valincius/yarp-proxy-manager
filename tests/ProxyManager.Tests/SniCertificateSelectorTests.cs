using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProxyManager.Application.Certificates;
using ProxyManager.Certificates;
using ProxyManager.Domain;
using Xunit;

namespace ProxyManager.Tests;

public sealed class SniCertificateSelectorTests : IDisposable
{
    private readonly string _dataDirectory;
    private readonly InMemoryCertificateRepository _repository = new();

    public SniCertificateSelectorTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sni-tests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Select_MatchesExactAndWildcardDomains()
    {
        var fileStore = new CertificateFileStore(_dataDirectory);
        var protector = new FakeSecretProtector();
        var wildcardPfx = CertificateManagerTests.SelfSignedCertificate.CreatePfx("*.example.com", "pw");
        var wildcard = new Certificate
        {
            Id = Guid.NewGuid(),
            Name = "Wildcard",
            Domains = ["*.example.com"],
            Provider = CertificateProvider.Manual,
            Status = CertificateStatus.Issued,
            EncryptedPfxPassword = protector.Protect("pw"),
            NotAfter = DateTimeOffset.UtcNow.AddDays(90),
        };
        wildcard.PfxPath = await fileStore.SavePfxAsync(wildcard.Id, wildcardPfx, CancellationToken.None);
        _repository.Certificates.Add(wildcard);

        var exactPfx = CertificateManagerTests.SelfSignedCertificate.CreatePfx("app.example.com", "pw");
        var exact = new Certificate
        {
            Id = Guid.NewGuid(),
            Name = "Exact",
            Domains = ["app.example.com"],
            Provider = CertificateProvider.Manual,
            Status = CertificateStatus.Issued,
            EncryptedPfxPassword = protector.Protect("pw"),
            NotAfter = DateTimeOffset.UtcNow.AddDays(90),
        };
        exact.PfxPath = await fileStore.SavePfxAsync(exact.Id, exactPfx, CancellationToken.None);
        _repository.Certificates.Add(exact);

        var selector = CreateSelector();
        await selector.ReloadAsync(CancellationToken.None);

        selector.Select(context: null, "app.example.com")!.Subject.Should().Contain("app.example.com");
        selector.Select(context: null, "api.example.com")!.Subject.Should().Contain("*.example.com");
        selector.Select(context: null, "other.com").Should().BeNull();
        selector.Select(context: null, null).Should().BeNull();
    }

    [Fact]
    public async Task Reload_SkipsExpiredAndNonIssuedCertificates()
    {
        var fileStore = new CertificateFileStore(_dataDirectory);
        var expiredPfx = CertificateManagerTests.SelfSignedCertificate.CreatePfx("expired.example.com", "pw");
        var expired = new Certificate
        {
            Id = Guid.NewGuid(),
            Name = "Expired",
            Domains = ["expired.example.com"],
            Provider = CertificateProvider.Manual,
            Status = CertificateStatus.Issued,
            EncryptedPfxPassword = new FakeSecretProtector().Protect("pw"),
            NotAfter = DateTimeOffset.UtcNow.AddDays(-5),
        };
        expired.PfxPath = await fileStore.SavePfxAsync(expired.Id, expiredPfx, CancellationToken.None);
        _repository.Certificates.Add(expired);

        var selector = CreateSelector();
        await selector.ReloadAsync(CancellationToken.None);

        selector.Select(context: null, "expired.example.com").Should().BeNull();
    }

    private SniCertificateSelector CreateSelector()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICertificateRepository>(_repository);
        services.AddSingleton(new CertificateFileStore(_dataDirectory));
        services.AddSingleton<ISecretProtector>(new FakeSecretProtector());
        var provider = services.BuildServiceProvider();
        return new SniCertificateSelector(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SniCertificateSelector>.Instance);
    }
}
