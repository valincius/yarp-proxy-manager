using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Certes.Acme;
using ProxyManager.Application;
using ProxyManager.Application.Certificates;
using ProxyManager.Application.Exceptions;
using ProxyManager.Certificates.Acme;
using ProxyManager.Domain;

namespace ProxyManager.Certificates;

/// <summary>
/// Certificate use-cases: ACME issuance (HTTP-01 / DNS-01), manual upload, renewal,
/// deletion, ACME account settings and DNS credentials.
/// </summary>
public sealed class CertificateManager(
    ICertificateRepository repository,
    ISecretProtector secrets,
    IDnsChallengeProviderFactory dnsProviderFactory,
    IAcmeClient acmeClient,
    Http01ChallengeStore challengeStore,
    CertificateFileStore fileStore,
    SniCertificateSelector sniSelector,
    IConfigReloadNotifier notifier,
    IssueCertificateValidator issueValidator,
    UploadCertificateValidator uploadValidator,
    DnsCredentialValidator dnsCredentialValidator,
    AcmeSettingsValidator acmeSettingsValidator,
    ILogger<CertificateManager> logger,
    TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    // --- Listing ---

    public Task<IReadOnlyList<Certificate>> ListAsync(CancellationToken cancellationToken = default)
        => repository.ListCertificatesAsync(cancellationToken);

    public Task<Certificate?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => repository.GetCertificateAsync(id, cancellationToken);

    // --- ACME issuance ---

    public async Task<Certificate> IssueAsync(IssueCertificateRequest request, CancellationToken cancellationToken = default)
    {
        await issueValidator.ValidateAndThrowAsync(request, cancellationToken);

        var now = _time.GetUtcNow();
        var certificate = new Certificate
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Domains = request.Domains.Select(DomainName.Normalize).ToList(),
            Provider = CertificateProvider.Acme,
            Status = CertificateStatus.Pending,
            ChallengeType = request.ChallengeType,
            DnsCredentialId = request.DnsCredentialId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await repository.AddCertificateAsync(certificate, cancellationToken);
        return await RunAcmeFlowAsync(certificate, request, cancellationToken);
    }

    public async Task<Certificate> RenewAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var certificate = await repository.GetCertificateAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Certificate '{id}' was not found.");

        if (certificate.Provider != CertificateProvider.Acme)
        {
            throw new InvalidOperationException("Only ACME-issued certificates can be renewed automatically.");
        }

        var request = new IssueCertificateRequest(
            certificate.Name,
            certificate.Domains,
            certificate.ChallengeType ?? "Http01",
            certificate.DnsCredentialId);

        return await RunAcmeFlowAsync(certificate, request, cancellationToken);
    }

    private async Task<Certificate> RunAcmeFlowAsync(
        Certificate certificate,
        IssueCertificateRequest request,
        CancellationToken cancellationToken)
    {
        IDnsChallengeProvider? dnsProvider = null;
        var handled = new List<(AcmeChallengeDescriptor Challenge, IDnsChallengeProvider? DnsProvider)>();
        try
        {
            var account = await repository.GetAcmeAccountAsync(cancellationToken)
                ?? throw new InvalidOperationException(
                    "No ACME account is configured. Set the account email and CA in Settings first.");

            if (request.ChallengeType == "Dns01")
            {
                if (request.DnsCredentialId is not { } credentialId)
                {
                    throw new InvalidOperationException("A DNS credential is required for DNS-01 challenges.");
                }

                var credential = await repository.GetDnsCredentialAsync(credentialId, cancellationToken)
                    ?? throw new NotFoundException($"DNS credential '{credentialId}' was not found.");
                dnsProvider = dnsProviderFactory.Create(credential.Provider, secrets.Unprotect(credential.EncryptedApiToken));
            }

            var now = _time.GetUtcNow();
            certificate.Status = CertificateStatus.Pending;
            certificate.LastRenewalAttempt = now;
            certificate.LastRenewalError = null;
            certificate.UpdatedAt = now;
            await repository.UpdateCertificateAsync(certificate, cancellationToken);

            await acmeClient.InitializeAsync(
                account.Email,
                account.DirectoryUrl,
                secrets.Unprotect(account.EncryptedAccountKey),
                cancellationToken);

            var orderId = await acmeClient.CreateOrderAsync(certificate.Domains.ToArray(), cancellationToken);
            var challenges = await acmeClient.GetPendingChallengesAsync(orderId, cancellationToken);

            foreach (var challenge in challenges)
            {
                if (request.ChallengeType == "Http01" && challenge.Type == "http-01")
                {
                    challengeStore.Set(challenge.Token, challenge.KeyAuthorization);
                    handled.Add((challenge, null));
                }
                else if (request.ChallengeType == "Dns01" && challenge.Type == "dns-01")
                {
                    await dnsProvider!.AddTxtRecordAsync(
                        challenge.Domain,
                        challenge.DnsRecordName!,
                        challenge.DnsRecordValue!,
                        cancellationToken);
                    handled.Add((challenge, dnsProvider));
                }
            }

            if (handled.Count == 0)
            {
                throw new InvalidOperationException(
                    $"The ACME CA did not offer '{request.ChallengeType}' challenges for the requested domains.");
            }

            foreach (var (challenge, _) in handled)
            {
                await acmeClient.ValidateChallengeAsync(orderId, challenge.Token, cancellationToken);
            }

            foreach (var (challenge, _) in handled)
            {
                await acmeClient.WaitForChallengeAsync(orderId, challenge.Token, TimeSpan.FromSeconds(120), cancellationToken);
            }

            var pfxPassword = GeneratePassword();
            var issued = await acmeClient.FinalizeAsync(
                orderId,
                certificate.Domains[0],
                certificate.Domains.ToArray(),
                pfxPassword,
                cancellationToken);

            certificate.Status = CertificateStatus.Issued;
            certificate.NotBefore = issued.NotBefore;
            certificate.NotAfter = issued.NotAfter;
            certificate.PfxPath = await fileStore.SavePfxAsync(certificate.Id, issued.Pfx, cancellationToken);
            certificate.EncryptedPfxPassword = secrets.Protect(pfxPassword);
            certificate.LastRenewalError = null;
            certificate.UpdatedAt = _time.GetUtcNow();
            await repository.UpdateCertificateAsync(certificate, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            certificate.Status = CertificateStatus.Failed;
            certificate.LastRenewalError = ex.Message;
            certificate.UpdatedAt = _time.GetUtcNow();
            await repository.UpdateCertificateAsync(certificate, cancellationToken);
            fileStore.Delete(certificate.Id);
            throw;
        }
        finally
        {
            foreach (var (challenge, provider) in handled)
            {
                try
                {
                    if (provider is not null)
                    {
                        await provider.RemoveTxtRecordAsync(
                            challenge.Domain,
                            challenge.DnsRecordName!,
                            challenge.DnsRecordValue!,
                            CancellationToken.None);
                    }
                    else
                    {
                        challengeStore.Remove(challenge.Token);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to clean up ACME challenge {Token}.", challenge.Token);
                }
            }
        }

        notifier.Notify();
        await sniSelector.ReloadAsync(cancellationToken);
        return certificate;
    }

    // --- Manual upload ---

    public async Task<Certificate> UploadAsync(UploadCertificateRequest request, CancellationToken cancellationToken = default)
    {
        await uploadValidator.ValidateAndThrowAsync(request, cancellationToken);

        byte[] pfx;
        string pfxPassword;
        DateTimeOffset notBefore;
        DateTimeOffset notAfter;

        if (request.PfxBase64 is not null)
        {
            pfx = Convert.FromBase64String(request.PfxBase64);
            pfxPassword = request.PfxPassword ?? string.Empty;
        }
        else
        {
            pfxPassword = GeneratePassword();
            using var certificate = X509Certificate2.CreateFromPem(request.CertificatePem!, request.PrivateKeyPem ?? string.Empty);
            pfx = certificate.Export(X509ContentType.Pfx, pfxPassword);
        }

        using (var parsed = X509CertificateLoader.LoadPkcs12(pfx, pfxPassword, X509KeyStorageFlags.EphemeralKeySet))
        {
            notBefore = parsed.NotBefore;
            notAfter = parsed.NotAfter;
        }

        var now = _time.GetUtcNow();
        var record = new Certificate
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Domains = request.Domains.Select(DomainName.Normalize).ToList(),
            Provider = CertificateProvider.Manual,
            Status = CertificateStatus.Issued,
            NotBefore = notBefore,
            NotAfter = notAfter,
            CreatedAt = now,
            UpdatedAt = now,
        };

        record.PfxPath = await fileStore.SavePfxAsync(record.Id, pfx, cancellationToken);
        record.EncryptedPfxPassword = secrets.Protect(pfxPassword);
        await repository.AddCertificateAsync(record, cancellationToken);

        notifier.Notify();
        await sniSelector.ReloadAsync(cancellationToken);
        return record;
    }

    // --- Deletion ---

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var certificate = await repository.GetCertificateAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Certificate '{id}' was not found.");

        await repository.DeleteCertificateAsync(certificate, cancellationToken);
        fileStore.Delete(id);

        notifier.Notify();
        await sniSelector.ReloadAsync(cancellationToken);
    }

    // --- Renewal support ---

    public async Task<IReadOnlyList<Certificate>> FindRenewableAsync(CancellationToken cancellationToken = default)
    {
        var certificates = await repository.ListCertificatesAsync(cancellationToken);
        return certificates.Where(c => CertificateRenewalWorker.IsDue(c, _time.GetUtcNow())).ToList();
    }

    // --- DNS credentials ---

    public Task<IReadOnlyList<DnsCredential>> ListDnsCredentialsAsync(CancellationToken cancellationToken = default)
        => repository.ListDnsCredentialsAsync(cancellationToken);

    public async Task<DnsCredential> CreateDnsCredentialAsync(DnsCredentialInput input, CancellationToken cancellationToken = default)
    {
        await dnsCredentialValidator.ValidateAndThrowAsync(input, cancellationToken);

        var credential = new DnsCredential
        {
            Id = Guid.NewGuid(),
            Name = input.Name.Trim(),
            Provider = "Cloudflare",
            EncryptedApiToken = secrets.Protect(input.ApiToken.Trim()),
            CreatedAt = _time.GetUtcNow(),
        };

        await repository.AddDnsCredentialAsync(credential, cancellationToken);
        return credential;
    }

    public async Task DeleteDnsCredentialAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var credential = await repository.GetDnsCredentialAsync(id, cancellationToken)
            ?? throw new NotFoundException($"DNS credential '{id}' was not found.");

        await repository.DeleteDnsCredentialAsync(credential, cancellationToken);
    }

    // --- ACME account settings ---

    public async Task<AcmeSettingsDto> GetAcmeSettingsAsync(CancellationToken cancellationToken = default)
    {
        var account = await repository.GetAcmeAccountAsync(cancellationToken);
        if (account is null)
        {
            return new AcmeSettingsDto(string.Empty, WellKnownServers.LetsEncryptV2.ToString(), Staging: false);
        }

        return new AcmeSettingsDto(
            account.Email,
            account.DirectoryUrl,
            account.DirectoryUrl == WellKnownServers.LetsEncryptStagingV2.ToString());
    }

    public async Task UpdateAcmeSettingsAsync(AcmeSettingsDto settings, CancellationToken cancellationToken = default)
    {
        await acmeSettingsValidator.ValidateAndThrowAsync(settings, cancellationToken);

        var account = await repository.GetAcmeAccountAsync(cancellationToken)
            ?? new AcmeAccount { Id = Guid.NewGuid(), CreatedAt = _time.GetUtcNow() };

        account.Email = settings.Email.Trim();
        account.DirectoryUrl = settings.Staging
            ? WellKnownServers.LetsEncryptStagingV2.ToString()
            : string.IsNullOrWhiteSpace(settings.DirectoryUrl)
                ? WellKnownServers.LetsEncryptV2.ToString()
                : settings.DirectoryUrl.Trim();
        account.UpdatedAt = _time.GetUtcNow();

        if (string.IsNullOrWhiteSpace(account.EncryptedAccountKey))
        {
            account.EncryptedAccountKey = secrets.Protect(GenerateAccountKeyPem());
        }

        await repository.UpsertAcmeAccountAsync(account, cancellationToken);
    }

    private static string GeneratePassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes);
    }

    private static string GenerateAccountKeyPem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportPkcs8PrivateKeyPem();
    }
}
