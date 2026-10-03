using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Orders;

// Confirmation and stock reservation of an order (FLOW_1 §6): Admin, Store Owner and Sales.
[ApiController]
[Route("api/orders/{id:guid}")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class OrderConfirmationController(IOrderConfirmationService service) : ControllerBase
{
    // FEFO's proposal for a pending order, or the open reservation lines of a confirmed one.
    [HttpGet("fefo-suggestions")]
    public async Task<ActionResult<FefoSuggestionResponse>> GetFefoSuggestions(Guid id, CancellationToken cancellationToken) =>
        await service.GetFefoSuggestionsAsync(id, cancellationToken);

    // One transaction: settlement check, FEFO, reservation, status. Not enough stock for any line → 422, nothing reserved.
    [HttpPost("confirm")]
    public async Task<ActionResult<OrderResponse>> Confirm(Guid id, CancellationToken cancellationToken) =>
        await service.ConfirmAsync(id, cancellationToken);

    [HttpPost("start-preparing")]
    public async Task<ActionResult<OrderResponse>> StartPreparing(Guid id, CancellationToken cancellationToken) =>
        await service.StartPreparingAsync(id, cancellationToken);

    [HttpPost("mark-ready")]
    public async Task<ActionResult<OrderResponse>> MarkReady(Guid id, CancellationToken cancellationToken) =>
        await service.MarkReadyAsync(id, cancellationToken);

    [HttpGet("reservation")]
    public async Task<ActionResult<ReservationResponse>> GetReservation(Guid id, CancellationToken cancellationToken) =>
        await service.GetReservationAsync(id, cancellationToken);
}
