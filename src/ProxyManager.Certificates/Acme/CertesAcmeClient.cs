using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Certes.Pkcs;
using Microsoft.Extensions.Logging;

namespace ProxyManager.Certificates.Acme;

/// <summary>Certes-backed <see cref="IAcmeClient"/>.</summary>
public sealed class CertesAcmeClient(ILogger<CertesAcmeClient> logger) : IAcmeClient
{
    private readonly Dictionary<string, IOrderContext> _orders = new();
    private AcmeContext? _context;

    public async Task InitializeAsync(string email, string directoryUrl, string accountKeyPem, CancellationToken cancellationToken)
    {
        var accountKey = KeyFactory.FromPem(accountKeyPem);
        _context = new AcmeContext(new Uri(directoryUrl), accountKey);

        try
        {
            await _context.Account();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No account exists for this key yet — create one. Certes' NewAccount expects
            // contact URIs; a raw email is rejected by the CA with
            // 'acme:error:unsupportedContact: only contact scheme mailto: is supported'.
            await _context.NewAccount([BuildAccountContact(email)], true);
        }
    }

    /// <summary>Certes NewAccount takes contact URIs — prefix the account email with <c>mailto:</c>.</summary>
    internal static string BuildAccountContact(string email) =>
        email.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? email : $"mailto:{email}";

    public async Task<string> CreateOrderAsync(string[] domains, CancellationToken cancellationToken)
    {
        var context = EnsureInitialized();
        var order = await context.NewOrder(domains);
        var orderId = Guid.NewGuid().ToString("N");
        _orders[orderId] = order;
        return orderId;
    }

    public async Task<IReadOnlyList<AcmeChallengeDescriptor>> GetPendingChallengesAsync(string orderId, CancellationToken cancellationToken)
    {
        var order = GetOrder(orderId);
        var descriptors = new List<AcmeChallengeDescriptor>();

        var authorizations = await order.Authorizations();
        foreach (var authorization in authorizations)
        {
            var resource = await authorization.Resource();
            var domain = resource.Identifier?.Value ?? string.Empty;

            var challenges = await authorization.Challenges();
            foreach (var challenge in challenges)
            {
                var type = challenge.Type;
                var token = challenge.Token;
                var keyAuthz = challenge.KeyAuthz;

                descriptors.Add(new AcmeChallengeDescriptor(
                    token,
                    type,
                    domain,
                    keyAuthz,
                    type == "dns-01" ? $"_acme-challenge.{domain}" : null,
                    type == "dns-01" ? ComputeDnsRecordValue(keyAuthz) : null));
            }
        }

        return descriptors;
    }

    public async Task ValidateChallengeAsync(string orderId, string token, CancellationToken cancellationToken)
    {
        var challenge = await FindChallengeAsync(GetOrder(orderId), token);
        if (challenge is null)
        {
            throw new InvalidOperationException($"Challenge '{token}' was not found on the ACME order.");
        }

        await challenge.Validate();
    }

    public async Task WaitForChallengeAsync(string orderId, string token, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var order = GetOrder(orderId);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var challenge = await FindChallengeAsync(order, token);
            if (challenge is null)
            {
                throw new InvalidOperationException($"Challenge '{token}' was not found on the ACME order.");
            }

            var resource = await challenge.Resource();
            switch (resource.Status)
            {
                case ChallengeStatus.Valid:
                    return;
                case ChallengeStatus.Invalid:
                    throw new InvalidOperationException($"ACME challenge '{token}' failed: {resource.Error?.Detail ?? "invalid"}");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new TimeoutException($"Timed out waiting for ACME challenge '{token}' to be validated.");
    }

    public async Task<AcmeIssuedCertificate> FinalizeAsync(
        string orderId,
        string commonName,
        string[] sanDomains,
        string pfxPassword,
        CancellationToken cancellationToken)
    {
        var order = GetOrder(orderId);

        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest($"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var sanBuilder = new SubjectAlternativeNameBuilder();
        foreach (var domain in sanDomains)
        {
            sanBuilder.AddDnsName(domain);
        }

        certificateRequest.CertificateExtensions.Add(sanBuilder.Build());
        var csrDer = certificateRequest.CreateSigningRequest();
        await order.Finalize(csrDer);

        var chain = await PollForCertificateAsync(order, cancellationToken);
        var leaf = X509CertificateLoader.LoadCertificate(chain.Certificate.ToDer());
        var privateKey = KeyFactory.FromPem(rsa.ExportPkcs8PrivateKeyPem());

        var pfxBuilder = new PfxBuilder(chain.Certificate.ToDer(), privateKey) { FullChain = true };
        foreach (var issuer in chain.Issuers)
        {
            pfxBuilder.AddIssuer(issuer.ToDer());
        }

        var pfx = pfxBuilder.Build(commonName, pfxPassword);
        return new AcmeIssuedCertificate(pfx, leaf.NotBefore, leaf.NotAfter);
    }

    public void Dispose()
    {
        _orders.Clear();
        _context = null;
    }

    private AcmeContext EnsureInitialized() =>
        _context ?? throw new InvalidOperationException("The ACME client has not been initialized.");

    private IOrderContext GetOrder(string orderId) =>
        _orders.TryGetValue(orderId, out var order)
            ? order
            : throw new InvalidOperationException($"Unknown ACME order '{orderId}'.");

    private static async Task<IChallengeContext?> FindChallengeAsync(IOrderContext order, string token)
    {
        var authorizations = await order.Authorizations();
        foreach (var authorization in authorizations)
        {
            var challenges = await authorization.Challenges();
            foreach (var challenge in challenges)
            {
                if (challenge.Token == token)
                {
                    return challenge;
                }
            }
        }

        return null;
    }

    private async Task<CertificateChain> PollForCertificateAsync(IOrderContext order, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await order.Download();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Order still processing — poll again.
                logger.LogDebug("Certificate not ready yet: {Message}", ex.Message);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new TimeoutException("Timed out waiting for the ACME order to be finalized.");
    }

    private static string ComputeDnsRecordValue(string keyAuthorization)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(keyAuthorization));
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
