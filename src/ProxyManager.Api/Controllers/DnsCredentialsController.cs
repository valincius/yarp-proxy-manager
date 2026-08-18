using Microsoft.AspNetCore.Mvc;
using ProxyManager.Application.Certificates;
using ProxyManager.Certificates;
using ProxyManager.Domain;

namespace ProxyManager.Api.Controllers;

[Route("api/v1/dns-credentials")]
public sealed class DnsCredentialsController(CertificateManager manager) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var credentials = await manager.ListDnsCredentialsAsync(cancellationToken);
        return Ok(credentials.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create(DnsCredentialInput input, CancellationToken cancellationToken)
    {
        var credential = await manager.CreateDnsCredentialAsync(input, cancellationToken);
        return Ok(ToDto(credential));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await manager.DeleteDnsCredentialAsync(id, cancellationToken);
        return NoContent();
    }

    private static DnsCredentialDto ToDto(DnsCredential credential) => new(
        credential.Id,
        credential.Name,
        credential.Provider,
        credential.CreatedAt);
}

public sealed record DnsCredentialDto(Guid Id, string Name, string Provider, DateTimeOffset CreatedAt);
