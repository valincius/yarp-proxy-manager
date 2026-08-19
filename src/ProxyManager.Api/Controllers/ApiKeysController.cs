using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProxyManager.Application.ApiKeys;

namespace ProxyManager.Api.Controllers;

/// <summary>
/// Management of REST API keys. Cookie-admin only — API keys cannot manage themselves.
/// </summary>
[Route("api/v1/api-keys")]
[Authorize(Roles = "Admin")]
public sealed class ApiKeysController(ApiKeyService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await service.ListAsync(cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(CreateApiKeyRequest request, CancellationToken cancellationToken)
    {
        var created = await service.CreateAsync(request, GetUserId(), cancellationToken);
        // The plaintext key is shown exactly once.
        return CreatedAtAction(nameof(List), created);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    private Guid? GetUserId()
        => Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id
            : null;
}
