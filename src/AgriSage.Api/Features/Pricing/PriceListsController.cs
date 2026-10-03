using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Pricing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Pricing;

// Price lists (FLOW_1 §3). Read: Admin, Store Owner, Sales. Write: Admin and Store Owner.
[ApiController]
[Route("api/price-lists")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class PriceListsController(IPriceListService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<PriceListResponse>>> List(
        [FromQuery] PriceListListRequest request, CancellationToken cancellationToken) =>
        await service.ListAsync(request, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PriceListResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await service.GetAsync(id, cancellationToken);

    // Creates a DRAFT list.
    [HttpPost]
    [Authorize(Roles = ApiRoles.Manage)]
    [ProducesResponseType<PriceListResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(PriceListRequest request, CancellationToken cancellationToken)
    {
        var response = await service.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<PriceListResponse>> Update(
        Guid id, UpdatePriceListRequest request, CancellationToken cancellationToken) =>
        await service.UpdateAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/activate")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<PriceListResponse>> Activate(Guid id, CancellationToken cancellationToken) =>
        await service.ActivateAsync(id, cancellationToken);

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<PriceListResponse>> Deactivate(Guid id, CancellationToken cancellationToken) =>
        await service.DeactivateAsync(id, cancellationToken);

    // Soft delete of a DRAFT list never linked to a group nor used by an order (409 otherwise).
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, cancellationToken);

        return NoContent();
    }

    [HttpGet("{id:guid}/items")]
    public async Task<ActionResult<PagedResult<PriceListItemResponse>>> ListItems(
        Guid id, [FromQuery] PriceListItemListRequest request, CancellationToken cancellationToken) =>
        await service.ListItemsAsync(id, request, cancellationToken);

    // Bulk upsert, at most 500 lines; answers how many lines were created and updated.
    [HttpPut("{id:guid}/items")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<UpsertPriceListItemsResult>> UpsertItems(
        Guid id, UpsertPriceListItemsRequest request, CancellationToken cancellationToken) =>
        await service.UpsertItemsAsync(id, request, cancellationToken);

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<IActionResult> DeleteItem(Guid id, Guid itemId, CancellationToken cancellationToken)
    {
        await service.DeleteItemAsync(id, itemId, cancellationToken);

        return NoContent();
    }
}
