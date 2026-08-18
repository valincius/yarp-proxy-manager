using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProxyManager.Application.Certificates;
using ProxyManager.Domain;

namespace ProxyManager.Certificates;

/// <summary>
/// Kestrel SNI certificate selector backed by the issued certificates. Matches the
/// SNI hostname exactly, then falls back to wildcard certificates for parent domains.
/// </summary>
public sealed class SniCertificateSelector(
    IServiceScopeFactory scopeFactory,
    ILogger<SniCertificateSelector> logger)
{
    private readonly ConcurrentDictionary<string, X509Certificate2> _certificates = new(StringComparer.OrdinalIgnoreCase);

    public X509Certificate2? Select(ConnectionContext? context, string? domainName)
    {
        if (string.IsNullOrWhiteSpace(domainName))
        {
            return null;
        }

        if (_certificates.TryGetValue(domainName, out var exact))
        {
            return exact;
        }

        var labels = domainName.Split('.');
        for (var i = 1; i < labels.Length - 1; i++)
        {
            var wildcard = "*." + string.Join('.', labels[i..]);
            if (_certificates.TryGetValue(wildcard, out var wildcardCert))
            {
                return wildcardCert;
            }
        }

        return null;
    }

    /// <summary>Reloads the in-memory certificate table from the database.</summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICertificateRepository>();
        var fileStore = scope.ServiceProvider.GetRequiredService<CertificateFileStore>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretProtector>();

        var certificates = await repository.ListCertificatesAsync(cancellationToken);
        var next = new Dictionary<string, X509Certificate2>(StringComparer.OrdinalIgnoreCase);

        foreach (var certificate in certificates.Where(c =>
                     c.Status == CertificateStatus.Issued
                     && c.PfxPath is not null
                     && c.EncryptedPfxPassword is not null
                     && (c.NotAfter is null || c.NotAfter > DateTimeOffset.UtcNow)))
        {
            try
            {
                var password = secrets.Unprotect(certificate.EncryptedPfxPassword!);
                var x509 = X509CertificateLoader.LoadPkcs12FromFile(
                    fileStore.GetPath(certificate.Id),
                    password,
                    X509KeyStorageFlags.EphemeralKeySet);

                foreach (var domain in certificate.Domains)
                {
                    next[domain] = x509;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to load certificate '{CertificateId}' for SNI selection.", certificate.Id);
            }
        }

        // Swap the tables; dispose entries that were removed.
        foreach (var (domain, certificate) in _certificates)
        {
            if (!next.ContainsKey(domain))
            {
                _certificates.TryRemove(domain, out _);
                certificate.Dispose();
            }
        }

        foreach (var (domain, certificate) in next)
        {
            _certificates[domain] = certificate;
        }

        logger.LogInformation("SNI certificate table loaded: {Count} domains.", _certificates.Count);
    }
}
