using ProxyManager.Application;
using ProxyManager.Application.Certificates;
using ProxyManager.Certificates.Acme;
using ProxyManager.Domain;

namespace ProxyManager.Tests;

public sealed class FakeSecretProtector : ISecretProtector
{
    public string Protect(string plainText) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plainText));

    public string Unprotect(string protectedText) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedText));
}

public sealed class FakeDnsChallengeProvider : IDnsChallengeProvider
{
    public List<(string Domain, string Name, string Value)> Added { get; } = [];
    public List<(string Domain, string Name, string Value)> Removed { get; } = [];

    public Task AddTxtRecordAsync(string domain, string recordName, string recordValue, CancellationToken cancellationToken = default)
    {
        Added.Add((domain, recordName, recordValue));
        return Task.CompletedTask;
    }

    public Task RemoveTxtRecordAsync(string domain, string recordName, string recordValue, CancellationToken cancellationToken = default)
    {
        Removed.Add((domain, recordName, recordValue));
        return Task.CompletedTask;
    }
}

public sealed class FakeDnsProviderFactory(FakeDnsChallengeProvider provider) : IDnsChallengeProviderFactory
{
    public IDnsChallengeProvider Create(string providerKey, string apiToken) => provider;
}

public sealed class NoopReloadNotifier : IConfigReloadNotifier
{
    public void Notify() { }
}

public sealed class FakeAcmeClient : IAcmeClient
{
    public IReadOnlyList<AcmeChallengeDescriptor> Challenges { get; set; } = [];
    public string? FailValidationToken { get; set; }
    public AcmeIssuedCertificate Issued { get; set; } = new(
        [1, 2, 3, 4],
        DateTimeOffset.UtcNow.AddDays(-1),
        DateTimeOffset.UtcNow.AddDays(90));
    public List<string> CreatedOrders { get; } = [];

    public Task InitializeAsync(string email, string directoryUrl, string accountKeyPem, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<string> CreateOrderAsync(string[] domains, CancellationToken cancellationToken)
    {
        CreatedOrders.AddRange(domains);
        return Task.FromResult("order-1");
    }

    public Task<IReadOnlyList<AcmeChallengeDescriptor>> GetPendingChallengesAsync(string orderId, CancellationToken cancellationToken)
        => Task.FromResult(Challenges);

    public Task ValidateChallengeAsync(string orderId, string token, CancellationToken cancellationToken)
    {
        if (token == FailValidationToken)
        {
            throw new InvalidOperationException("Validation failed (scripted).");
        }

        return Task.CompletedTask;
    }

    public Task WaitForChallengeAsync(string orderId, string token, TimeSpan timeout, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<AcmeIssuedCertificate> FinalizeAsync(string orderId, string commonName, string[] sanDomains, string pfxPassword, CancellationToken cancellationToken)
        => Task.FromResult(Issued);

    public void Dispose() { }
}

public sealed class InMemoryCertificateRepository : ICertificateRepository
{
    public List<Certificate> Certificates { get; } = [];
    public List<DnsCredential> DnsCredentials { get; } = [];
    public AcmeAccount? Account { get; set; }

    public Task<IReadOnlyList<Certificate>> ListCertificatesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Certificate>>(Certificates.ToList());

    public Task<Certificate?> GetCertificateAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Certificates.FirstOrDefault(c => c.Id == id));

    public Task AddCertificateAsync(Certificate certificate, CancellationToken cancellationToken = default)
    {
        Certificates.Add(certificate);
        return Task.CompletedTask;
    }

    public Task UpdateCertificateAsync(Certificate certificate, CancellationToken cancellationToken = default)
    {
        var index = Certificates.FindIndex(c => c.Id == certificate.Id);
        if (index >= 0)
        {
            Certificates[index] = certificate;
        }

        return Task.CompletedTask;
    }

    public Task DeleteCertificateAsync(Certificate certificate, CancellationToken cancellationToken = default)
    {
        Certificates.RemoveAll(c => c.Id == certificate.Id);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DnsCredential>> ListDnsCredentialsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DnsCredential>>(DnsCredentials.ToList());

    public Task<DnsCredential?> GetDnsCredentialAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(DnsCredentials.FirstOrDefault(c => c.Id == id));

    public Task AddDnsCredentialAsync(DnsCredential credential, CancellationToken cancellationToken = default)
    {
        DnsCredentials.Add(credential);
        return Task.CompletedTask;
    }

    public Task DeleteDnsCredentialAsync(DnsCredential credential, CancellationToken cancellationToken = default)
    {
        DnsCredentials.RemoveAll(c => c.Id == credential.Id);
        return Task.CompletedTask;
    }

    public Task<AcmeAccount?> GetAcmeAccountAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Account);

    public Task UpsertAcmeAccountAsync(AcmeAccount account, CancellationToken cancellationToken = default)
    {
        Account = account;
        return Task.CompletedTask;
    }
}
