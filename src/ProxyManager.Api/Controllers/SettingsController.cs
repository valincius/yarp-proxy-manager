using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProxyManager.Application.Settings;
using ProxyManager.Infrastructure.Docker;

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

    [HttpGet("docker")]
    public async Task<IActionResult> GetDocker(CancellationToken cancellationToken)
        => Ok(await settings.GetDockerSettingsAsync(cancellationToken));

    [HttpPut("docker")]
    public async Task<IActionResult> SetDocker(DockerSettingsInput input, CancellationToken cancellationToken)
    {
        await settings.SetDockerSettingsAsync(input, cancellationToken);
        return Ok(await settings.GetDockerSettingsAsync(cancellationToken));
    }

    [HttpPost("docker/sync")]
    public async Task<IActionResult> SyncDocker(CancellationToken cancellationToken)
    {
        var sync = HttpContext.RequestServices.GetRequiredService<DockerHostSyncService>();
        await sync.SyncAsync(cancellationToken);
        return Ok(await settings.GetDockerSettingsAsync(cancellationToken));
    }

    [HttpGet("diagnostics")]
    public async Task<IActionResult> GetDiagnostics(CancellationToken cancellationToken)
        => Ok(await settings.GetDiagnosticsSettingsAsync(cancellationToken));

    [HttpPut("diagnostics")]
    public async Task<IActionResult> SetDiagnostics(DiagnosticsSettingsInput input, CancellationToken cancellationToken)
    {
        await settings.SetDiagnosticsSettingsAsync(input, cancellationToken);
        return Ok(await settings.GetDiagnosticsSettingsAsync(cancellationToken));
    }
}
