using Microsoft.AspNetCore.Mvc;
using ProxyManager.Application.ProxyHosts;

namespace ProxyManager.Api.Controllers;

[Route("api/v1/hosts")]
public sealed class ProxyHostsController(ProxyHostService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await service.ListAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var host = await service.GetAsync(id, cancellationToken);
        return host is null ? NotFound() : Ok(host);
    }

    [HttpPost]
    public async Task<IActionResult> Create(ProxyHostInput input, CancellationToken cancellationToken)
    {
        var host = await service.CreateAsync(input, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = host.Id }, host);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, ProxyHostInput input, CancellationToken cancellationToken)
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

public sealed record SetEnabledRequest(bool Enabled);
