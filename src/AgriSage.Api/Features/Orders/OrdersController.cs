using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Orders;

// Staff counter orders (FLOW_1 §4): Admin, Store Owner and Sales. Confirmation, payments and fulfillment are other routes.
[ApiController]
[Route("api/orders")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class OrdersController(IOrderService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateCounterOrderRequest request, CancellationToken cancellationToken)
    {
        var response = await service.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderListItem>>> List(
        [FromQuery] OrderListRequest request, CancellationToken cancellationToken) =>
        await service.ListAsync(request, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await service.GetAsync(id, cancellationToken);

    // Note and delivery address, only while PENDING_CONFIRMATION.
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OrderResponse>> Update(
        Guid id, UpdateOrderRequest request, CancellationToken cancellationToken) =>
        await service.UpdateAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/items")]
    public async Task<ActionResult<OrderResponse>> AddItem(
        Guid id, OrderItemRequest request, CancellationToken cancellationToken) =>
        await service.AddItemAsync(id, request, cancellationToken);

    [HttpPut("{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<OrderResponse>> ChangeItemQuantity(
        Guid id, Guid itemId, ChangeOrderItemQuantityRequest request, CancellationToken cancellationToken) =>
        await service.ChangeItemQuantityAsync(id, itemId, request, cancellationToken);

    // Price override: a reason is required and the suggested price, actor and reason are kept and audited.
    [HttpPut("{id:guid}/items/{itemId:guid}/price")]
    public async Task<ActionResult<OrderResponse>> OverrideItemPrice(
        Guid id, Guid itemId, OverrideOrderItemPriceRequest request, CancellationToken cancellationToken) =>
        await service.OverrideItemPriceAsync(id, itemId, request, cancellationToken);

    // Back to the suggested price.
    [HttpDelete("{id:guid}/items/{itemId:guid}/price")]
    public async Task<ActionResult<OrderResponse>> RestoreItemPrice(
        Guid id, Guid itemId, CancellationToken cancellationToken) =>
        await service.RestoreItemPriceAsync(id, itemId, cancellationToken);

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<OrderResponse>> RemoveItem(
        Guid id, Guid itemId, CancellationToken cancellationToken) =>
        await service.RemoveItemAsync(id, itemId, cancellationToken);
}
