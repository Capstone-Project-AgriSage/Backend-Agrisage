using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Orders;

// Cancelling an order (FLOW_1 §8): Admin, Store Owner and Sales.
[ApiController]
[Route("api/orders/{id:guid}/cancel")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class OrderCancellationController(IOrderCancellationService service) : ControllerBase
{
    // Only before anything was fulfilled. Releases the reservation, gives paid-but-unused money back as PENDING refunds
    // (completed with the refund routes) and answers with the order and what staff must hand back.
    [HttpPost]
    public async Task<ActionResult<OrderCancellationResponse>> Cancel(
        Guid id, CancelOrderRequest request, CancellationToken cancellationToken) =>
        await service.CancelAsync(id, request, cancellationToken);
}
