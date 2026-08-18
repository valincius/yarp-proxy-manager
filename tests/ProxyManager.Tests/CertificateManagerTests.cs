using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProxyManager.Application.Certificates;
using ProxyManager.Certificates;
using ProxyManager.Certificates.Acme;
using ProxyManager.Domain;
using Xunit;

namespace ProxyManager.Tests;

public sealed class CertificateManagerTests : IDisposable
{
    private readonly InMemoryCertificateRepository _repository = new();
    private readonly FakeAcmeClient _acme = new();
    private readonly FakeDnsChallengeProvider _dns = new();
    private readonly Http01ChallengeStore _challenges = new();
    private readonly string _dataDirectory;
    private readonly SniCertificateSelector _selector;
    private readonly CertificateManager _manager;

    public CertificateManagerTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "cert-tests-" + Guid.NewGuid().ToString("N"));
        _selector = CreateSelector(_repository, _dataDirectory);
        _manager = new CertificateManager(
            _repository,
            new FakeSecretProtector(),
            new FakeDnsProviderFactory(_dns),
            _acme,
            _challenges,
            new CertificateFileStore(_dataDirectory),
            _selector,
            new NoopReloadNotifier(),
            new IssueCertificateValidator(),
            new UploadCertificateValidator(),
            new DnsCredentialValidator(),
            new AcmeSettingsValidator(),
            NullLogger<CertificateManager>.Instance,
            TimeProvider.System);
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

    private static SniCertificateSelector CreateSelector(InMemoryCertificateRepository repository, string dataDirectory)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICertificateRepository>(repository);
        services.AddSingleton(new CertificateFileStore(dataDirectory));
        services.AddSingleton<ISecretProtector>(new FakeSecretProtector());
        var provider = services.BuildServiceProvider();
        return new SniCertificateSelector(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SniCertificateSelector>.Instance);
    }

    private void SeedAcmeAccount() => _repository.Account = new AcmeAccount
    {
        Id = Guid.NewGuid(),
        Email = "admin@example.com",
        EncryptedAccountKey = new FakeSecretProtector().Protect("key"),
        DirectoryUrl = "https://acme-staging-v02.api.letsencrypt.org/directory",
    };

    [Fact]
    public async Task IssueAsync_Http01_IssuesCertificateAndCleansUp()
    {
        SeedAcmeAccount();
        _acme.Challenges =
        [
            new AcmeChallengeDescriptor("token-1", "http-01", "app.example.com", "keyauthz-1", null, null),
        ];

        var certificate = await _manager.IssueAsync(new IssueCertificateRequest(
            "My cert", ["app.example.com"], "Http01", DnsCredentialId: null), CancellationToken.None);

        certificate.Status.Should().Be(CertificateStatus.Issued);
        certificate.Provider.Should().Be(CertificateProvider.Acme);
        certificate.PfxPath.Should().NotBeNull();
        certificate.EncryptedPfxPassword.Should().NotBeNull();
        certificate.NotAfter.Should().Be(_acme.Issued.NotAfter);
        certificate.LastRenewalError.Should().BeNull();
        File.Exists(Path.Combine(_dataDirectory, "certs", $"{certificate.Id:n}.pfx")).Should().BeTrue();

        // The HTTP-01 challenge token was registered and cleaned up afterwards.
        _challenges.TryGetValue("token-1", out _).Should().BeFalse();
    }

    [Fact]
    public async Task IssueAsync_Dns01_AddsAndRemovesTxtRecords()
    {
        SeedAcmeAccount();
        var credential = new DnsCredential
        {
            Id = Guid.NewGuid(),
            Name = "Cloudflare",
            Provider = "Cloudflare",
            EncryptedApiToken = new FakeSecretProtector().Protect("token"),
        };
        _repository.DnsCredentials.Add(credential);
        _acme.Challenges =
        [
            new AcmeChallengeDescriptor("token-2", "dns-01", "*.example.com", "keyauthz-2",
                "_acme-challenge.example.com", "txt-value-2"),
        ];

        await _manager.IssueAsync(new IssueCertificateRequest(
            "Wildcard", ["*.example.com"], "Dns01", credential.Id), CancellationToken.None);

        _dns.Added.Should().ContainSingle(x =>
            x.Domain == "*.example.com" && x.Name == "_acme-challenge.example.com" && x.Value == "txt-value-2");
        _dns.Removed.Should().ContainSingle(x =>
            x.Domain == "*.example.com" && x.Name == "_acme-challenge.example.com" && x.Value == "txt-value-2");
    }

    [Fact]
    public async Task IssueAsync_WithoutAcmeAccount_ThrowsAndMarksFailed()
    {
        _acme.Challenges =
        [
            new AcmeChallengeDescriptor("token-3", "http-01", "app.example.com", "ka", null, null),
        ];

        var act = async () => await _manager.IssueAsync(new IssueCertificateRequest(
            "No account", ["app.example.com"], "Http01", null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var certificate = _repository.Certificates.Should().ContainSingle().Subject;
        certificate.Status.Should().Be(CertificateStatus.Failed);
        certificate.LastRenewalError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task IssueAsync_FailedChallenge_ThrowsAndMarksFailed()
    {
        SeedAcmeAccount();
        _acme.FailValidationToken = "token-bad";
        _acme.Challenges =
        [
            new AcmeChallengeDescriptor("token-bad", "http-01", "app.example.com", "ka", null, null),
        ];

        var act = async () => await _manager.IssueAsync(new IssueCertificateRequest(
            "Fails", ["app.example.com"], "Http01", null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var certificate = _repository.Certificates.Should().ContainSingle().Subject;
        certificate.Status.Should().Be(CertificateStatus.Failed);
        certificate.LastRenewalError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RenewAsync_ReissuesWithStoredSettings()
    {
        SeedAcmeAccount();
        _acme.Challenges =
        [
            new AcmeChallengeDescriptor("token-4", "http-01", "app.example.com", "ka", null, null),
        ];
        var original = await _manager.IssueAsync(new IssueCertificateRequest(
            "Renew me", ["app.example.com"], "Http01", null), CancellationToken.None);

        var renewed = await _manager.RenewAsync(original.Id, CancellationToken.None);

        renewed.Id.Should().Be(original.Id);
        renewed.Status.Should().Be(CertificateStatus.Issued);
        _acme.CreatedOrders.Should().Contain("app.example.com");
    }

    [Fact]
    public async Task RenewAsync_ManualCertificate_Throws()
    {
        var manual = new Certificate
        {
            Id = Guid.NewGuid(),
            Name = "Manual",
            Domains = ["app.example.com"],
            Provider = CertificateProvider.Manual,
            Status = CertificateStatus.Issued,
        };
        _repository.Certificates.Add(manual);

        var act = async () => await _manager.RenewAsync(manual.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UploadAsync_FromPem_CreatesManualCertificate()
    {
        var (certPem, keyPem) = SelfSignedCertificate.Create("manual.example.com");

        var certificate = await _manager.UploadAsync(new UploadCertificateRequest(
            "Manual PEM", ["manual.example.com"], PfxBase64: null, PfxPassword: null,
            CertificatePem: certPem, PrivateKeyPem: keyPem), CancellationToken.None);

        certificate.Provider.Should().Be(CertificateProvider.Manual);
        certificate.Status.Should().Be(CertificateStatus.Issued);
        certificate.PfxPath.Should().NotBeNull();
        File.Exists(Path.Combine(_dataDirectory, "certs", $"{certificate.Id:n}.pfx")).Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_FromPfxBase64_CreatesManualCertificate()
    {
        var pfx = SelfSignedCertificate.CreatePfx("pfx.example.com", "pw123");
        var certificate = await _manager.UploadAsync(new UploadCertificateRequest(
            "Manual PFX", ["pfx.example.com"], Convert.ToBase64String(pfx), "pw123",
            CertificatePem: null, PrivateKeyPem: null), CancellationToken.None);

        certificate.Provider.Should().Be(CertificateProvider.Manual);
        certificate.Status.Should().Be(CertificateStatus.Issued);
    }

    [Fact]
    public async Task DeleteAsync_RemovesRecordAndPfxFile()
    {
        var (certPem, keyPem) = SelfSignedCertificate.Create("del.example.com");
        var certificate = await _manager.UploadAsync(new UploadCertificateRequest(
            "Delete me", ["del.example.com"], null, null, certPem, keyPem), CancellationToken.None);
        var pfxPath = Path.Combine(_dataDirectory, "certs", $"{certificate.Id:n}.pfx");
        File.Exists(pfxPath).Should().BeTrue();

        await _manager.DeleteAsync(certificate.Id, CancellationToken.None);

        _repository.Certificates.Should().BeEmpty();
        File.Exists(pfxPath).Should().BeFalse();
    }

    [Fact]
    public async Task DnsCredential_IsStoredEncryptedAndNeverReturned()
    {
        var credential = await _manager.CreateDnsCredentialAsync(new DnsCredentialInput("CF", "secret-token"), CancellationToken.None);

        credential.EncryptedApiToken.Should().NotBe("secret-token");
        (await _manager.ListDnsCredentialsAsync(CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task AcmeSettings_UpdateCreatesAccountWithProtectedKey()
    {
        await _manager.UpdateAcmeSettingsAsync(new AcmeSettingsDto("me@example.com", string.Empty, Staging: true), CancellationToken.None);

        _repository.Account.Should().NotBeNull();
        var account = _repository.Account!;
        account.Email.Should().Be("me@example.com");
        account.DirectoryUrl.Should().Contain("staging");
        account.EncryptedAccountKey.Should().NotBeNullOrWhiteSpace();

        var settings = await _manager.GetAcmeSettingsAsync(CancellationToken.None);
        settings.Staging.Should().BeTrue();
        settings.Email.Should().Be("me@example.com");
    }

    public static class SelfSignedCertificate
    {
        private static System.Security.Cryptography.X509Certificates.CertificateRequest CreateRequest(
            string domain,
            System.Security.Cryptography.RSA rsa)
        {
            var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                $"CN={domain}", rsa,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pkcs1);
            var sanBuilder = new System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName(domain);
            request.CertificateExtensions.Add(sanBuilder.Build());
            request.CertificateExtensions.Add(new System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension(
                new System.Security.Cryptography.OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
            return request;
        }

        public static (string CertPem, string KeyPem) Create(string domain)
        {
            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            using var certificate = CreateRequest(domain, rsa).CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(90));
            return (certificate.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem());
        }

        public static byte[] CreatePfx(string domain, string password)
        {
            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            using var certificate = CreateRequest(domain, rsa).CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(90));
            return certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, password);
        }
    }
}
