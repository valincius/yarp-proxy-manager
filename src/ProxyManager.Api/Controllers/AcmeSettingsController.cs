using Microsoft.AspNetCore.Mvc;
using ProxyManager.Application.Certificates;
using ProxyManager.Certificates;

namespace ProxyManager.Api.Controllers;

[Route("api/v1/acme-settings")]
public sealed class AcmeSettingsController(CertificateManager manager) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => Ok(await manager.GetAcmeSettingsAsync(cancellationToken));

    [HttpPut]
    public async Task<IActionResult> Update(AcmeSettingsDto settings, CancellationToken cancellationToken)
    {
        await manager.UpdateAcmeSettingsAsync(settings, cancellationToken);
        return Ok(await manager.GetAcmeSettingsAsync(cancellationToken));
    }
}
