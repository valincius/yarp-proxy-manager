using Microsoft.AspNetCore.Mvc;
using ProxyManager.Application.Certificates;
using ProxyManager.Certificates;
using ProxyManager.Domain;

namespace ProxyManager.Api.Controllers;

[Route("api/v1/certificates")]
public sealed class CertificatesController(CertificateManager manager) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var certificates = await manager.ListAsync(cancellationToken);
        return Ok(certificates.Select(ToDto).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var certificate = await manager.GetAsync(id, cancellationToken);
        return certificate is null ? NotFound() : Ok(ToDto(certificate));
    }

    [HttpPost("issue")]
    public async Task<IActionResult> Issue(IssueCertificateRequest request, CancellationToken cancellationToken)
    {
        var certificate = await manager.IssueAsync(request, cancellationToken);
        return Ok(ToDto(certificate));
    }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload(UploadCertificateRequest request, CancellationToken cancellationToken)
    {
        var certificate = await manager.UploadAsync(request, cancellationToken);
        return Ok(ToDto(certificate));
    }

    [HttpPost("{id:guid}/renew")]
    public async Task<IActionResult> Renew(Guid id, CancellationToken cancellationToken)
    {
        var certificate = await manager.RenewAsync(id, cancellationToken);
        return Ok(ToDto(certificate));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await manager.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    internal static CertificateDto ToDto(Certificate certificate) => new(
        certificate.Id,
        certificate.Name,
        certificate.Domains.ToArray(),
        certificate.Provider.ToString(),
        certificate.Status.ToString(),
        certificate.NotBefore,
        certificate.NotAfter,
        certificate.ChallengeType,
        certificate.DnsCredentialId,
        certificate.LastRenewalAttempt,
        certificate.LastRenewalError,
        certificate.CreatedAt,
        certificate.UpdatedAt);
}

public sealed record CertificateDto(
    Guid Id,
    string Name,
    string[] Domains,
    string Provider,
    string Status,
    DateTimeOffset? NotBefore,
    DateTimeOffset? NotAfter,
    string? ChallengeType,
    Guid? DnsCredentialId,
    DateTimeOffset? LastRenewalAttempt,
    string? LastRenewalError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
