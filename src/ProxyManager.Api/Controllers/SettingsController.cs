using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProxyManager.Application.Settings;

namespace ProxyManager.Api.Controllers;

/// <summary>Application settings (proxy behavior, misc). Cookie-admin only.</summary>
[Route("api/v1/settings")]
[Authorize(Roles = "Admin")]
public sealed class SettingsController(SettingsService settings) : ApiControllerBase
{
    [HttpGet("not-found")]
    public async Task<IActionResult> GetNotFound(CancellationToken cancellationToken)
        => Ok(await settings.GetNotFoundSettingsAsync(cancellationToken));

    [HttpPut("not-found")]
    public async Task<IActionResult> SetNotFound(NotFoundSettingsInput input, CancellationToken cancellationToken)
        => Ok(await settings.SetNotFoundSettingsAsync(input, cancellationToken));
}
