using Microsoft.AspNetCore.Mvc;
using ProxyManager.Application.Streams;
using ProxyManager.Streams;

namespace ProxyManager.Api.Controllers;

[Route("api/v1/streams")]
public sealed class StreamsController(
    StreamService service,
    StreamStatusRegistry statusRegistry) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await service.ListAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var stream = await service.GetAsync(id, cancellationToken);
        return stream is null ? NotFound() : Ok(stream);
    }

    [HttpPost]
    public async Task<IActionResult> Create(StreamInput input, CancellationToken cancellationToken)
    {
        var stream = await service.CreateAsync(input, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = stream.Id }, stream);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, StreamInput input, CancellationToken cancellationToken)
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

    [HttpGet("status")]
    public IActionResult Statuses() => Ok(statusRegistry.Snapshot());

    [HttpGet("{id:guid}/status")]
    public IActionResult Status(Guid id)
        => statusRegistry.TryGet(id, out var status) ? Ok(status) : NotFound();
}
