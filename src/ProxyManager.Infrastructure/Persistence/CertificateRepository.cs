using Microsoft.EntityFrameworkCore;
using ProxyManager.Application.Certificates;
using ProxyManager.Domain;

namespace ProxyManager.Infrastructure.Persistence;

public sealed class CertificateRepository(ProxyDbContext db) : ICertificateRepository
{
    public async Task<IReadOnlyList<Certificate>> ListCertificatesAsync(CancellationToken cancellationToken = default) =>
        await db.Certificates.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken);

    public async Task<Certificate?> GetCertificateAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.Certificates.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task AddCertificateAsync(Certificate certificate, CancellationToken cancellationToken = default)
    {
        db.Certificates.Add(certificate);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateCertificateAsync(Certificate certificate, CancellationToken cancellationToken = default)
    {
        db.Certificates.Update(certificate);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteCertificateAsync(Certificate certificate, CancellationToken cancellationToken = default)
    {
        db.Certificates.Remove(certificate);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DnsCredential>> ListDnsCredentialsAsync(CancellationToken cancellationToken = default) =>
        await db.DnsCredentials.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken);

    public async Task<DnsCredential?> GetDnsCredentialAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.DnsCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task AddDnsCredentialAsync(DnsCredential credential, CancellationToken cancellationToken = default)
    {
        db.DnsCredentials.Add(credential);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteDnsCredentialAsync(DnsCredential credential, CancellationToken cancellationToken = default)
    {
        db.DnsCredentials.Remove(credential);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AcmeAccount?> GetAcmeAccountAsync(CancellationToken cancellationToken = default) =>
        await db.AcmeAccounts.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

    public async Task UpsertAcmeAccountAsync(AcmeAccount account, CancellationToken cancellationToken = default)
    {
        var existing = await db.AcmeAccounts.FirstOrDefaultAsync(cancellationToken);
        if (existing is null)
        {
            db.AcmeAccounts.Add(account);
        }
        else
        {
            existing.Email = account.Email;
            existing.EncryptedAccountKey = account.EncryptedAccountKey;
            existing.DirectoryUrl = account.DirectoryUrl;
            existing.UpdatedAt = account.UpdatedAt;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
