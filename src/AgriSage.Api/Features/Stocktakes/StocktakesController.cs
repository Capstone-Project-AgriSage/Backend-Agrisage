using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Stocktakes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Stocktakes;

[ApiController]
[Route("api/stocktakes")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class StocktakesController(IStocktakeService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<StocktakeResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateStocktakeRequest request, CancellationToken token)
    {
        var response = await service.CreateAsync(request, token);
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<StocktakeListItem>>> List([FromQuery] StocktakeListRequest request, CancellationToken token) =>
        await service.ListAsync(request, token);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StocktakeResponse>> Get(Guid id, [FromQuery] StocktakeDetailRequest request, CancellationToken token) =>
        await service.GetAsync(id, request, token);

    [HttpPost("{id:guid}/start")]
    public async Task<ActionResult<StocktakeResponse>> Start(Guid id, CancellationToken token) => await service.StartAsync(id, token);

    [HttpPut("{id:guid}/counts")]
    public async Task<ActionResult<StocktakeResponse>> Count(Guid id, StocktakeCountsRequest request, CancellationToken token) =>
        await service.CountAsync(id, request, token);

    [HttpPost("{id:guid}/refresh-stale")]
    public async Task<ActionResult<StocktakeResponse>> RefreshStale(Guid id, CancellationToken token) => await service.RefreshStaleAsync(id, token);

    [HttpPost("{id:guid}/complete")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<StocktakeResponse>> Complete(Guid id, CancellationToken token) => await service.CompleteAsync(id, token);

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<StocktakeResponse>> Cancel(Guid id, CancelStocktakeRequest request, CancellationToken token) =>
        await service.CancelAsync(id, request, token);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken token)
    {
        await service.DeleteAsync(id, token);
        return NoContent();
    }
}
