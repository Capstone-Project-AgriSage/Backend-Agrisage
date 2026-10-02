using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.GoodsReceipts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.GoodsReceipts;

// Draft work: Admin, Store Owner, Sales. Confirming changes stock and cost, so only Admin and Store Owner may do it.
[ApiController]
[Route("api/goods-receipts")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class GoodsReceiptsController(IGoodsReceiptService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<GoodsReceiptListItem>>> List(
        [FromQuery] GoodsReceiptListRequest request, CancellationToken cancellationToken) =>
        await service.ListAsync(request, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GoodsReceiptResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await service.GetAsync(id, cancellationToken);

    [HttpPost]
    [ProducesResponseType<GoodsReceiptResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateGoodsReceiptRequest request, CancellationToken cancellationToken)
    {
        var response = await service.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<GoodsReceiptResponse>> UpdateHeader(
        Guid id, UpdateGoodsReceiptRequest request, CancellationToken cancellationToken) =>
        await service.UpdateHeaderAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/items")]
    public async Task<ActionResult<GoodsReceiptResponse>> AddItem(
        Guid id, GoodsReceiptItemRequest request, CancellationToken cancellationToken) =>
        await service.AddItemAsync(id, request, cancellationToken);

    [HttpPut("{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<GoodsReceiptResponse>> UpdateItem(
        Guid id, Guid itemId, UpdateGoodsReceiptItemRequest request, CancellationToken cancellationToken) =>
        await service.UpdateItemAsync(id, itemId, request, cancellationToken);

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<GoodsReceiptResponse>> RemoveItem(
        Guid id, Guid itemId, CancellationToken cancellationToken) =>
        await service.RemoveItemAsync(id, itemId, cancellationToken);

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<GoodsReceiptResponse>> Cancel(
        Guid id, CancelGoodsReceiptRequest request, CancellationToken cancellationToken) =>
        await service.CancelAsync(id, request, cancellationToken);

    // Soft delete of a DRAFT receipt.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, cancellationToken);

        return NoContent();
    }

    // Creates/finds lots, adds stock at cost, posts STOCK_IN, marks the receipt CONFIRMED. Admin and Store Owner only.
    [HttpPost("{id:guid}/confirm")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<GoodsReceiptResponse>> Confirm(Guid id, CancellationToken cancellationToken) =>
        await service.ConfirmAsync(id, cancellationToken);
}
