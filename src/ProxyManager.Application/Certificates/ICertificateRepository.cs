using ProxyManager.Domain;

namespace ProxyManager.Application.Certificates;

public interface ICertificateRepository
{
    Task<IReadOnlyList<Certificate>> ListCertificatesAsync(CancellationToken cancellationToken = default);

    Task<Certificate?> GetCertificateAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddCertificateAsync(Certificate certificate, CancellationToken cancellationToken = default);

    Task UpdateCertificateAsync(Certificate certificate, CancellationToken cancellationToken = default);

    Task DeleteCertificateAsync(Certificate certificate, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DnsCredential>> ListDnsCredentialsAsync(CancellationToken cancellationToken = default);

    Task<DnsCredential?> GetDnsCredentialAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddDnsCredentialAsync(DnsCredential credential, CancellationToken cancellationToken = default);

    Task DeleteDnsCredentialAsync(DnsCredential credential, CancellationToken cancellationToken = default);

    Task<AcmeAccount?> GetAcmeAccountAsync(CancellationToken cancellationToken = default);

    Task UpsertAcmeAccountAsync(AcmeAccount account, CancellationToken cancellationToken = default);
}
