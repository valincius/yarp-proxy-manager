using Microsoft.AspNetCore.Mvc;
using ProxyManager.Application.Redirects;

namespace ProxyManager.Api.Controllers;

[Route("api/v1/redirects")]
public sealed class RedirectsController(RedirectHostService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await service.ListAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var redirect = await service.GetAsync(id, cancellationToken);
        return redirect is null ? NotFound() : Ok(redirect);
    }

    [HttpPost]
    public async Task<IActionResult> Create(RedirectHostInput input, CancellationToken cancellationToken)
    {
        var redirect = await service.CreateAsync(input, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = redirect.Id }, redirect);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, RedirectHostInput input, CancellationToken cancellationToken)
        => Ok(await service.UpdateAsync(id, input, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id:guid}/enable")]
    public async Task<IActionResult> SetEnabled(Guid id, SetEnabledRequest request, CancellationToken cancellationToken)
    {
        await service.SetEnabledAsync(id, request.Enabled, cancellationToken);
        return NoContent();
    }
}

[Route("api/v1/access-lists")]
public sealed class AccessListsController(AccessListService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await service.ListAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var accessList = await service.GetAsync(id, cancellationToken);
        return accessList is null ? NotFound() : Ok(accessList);
    }

    [HttpPost]
    public async Task<IActionResult> Create(AccessListInput input, CancellationToken cancellationToken)
    {
        var accessList = await service.CreateAsync(input, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = accessList.Id }, accessList);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, AccessListInput input, CancellationToken cancellationToken)
        => Ok(await service.UpdateAsync(id, input, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}

[Route("api/v1/audit")]
public sealed class AuditController(IAuditLogRepository repository) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int limit = 100,
        [FromQuery] string? entityType = null,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 500);
        return Ok(await repository.ListAsync(limit, entityType, cancellationToken));
    }
}
