using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Orders;

// The Farmer's online checkout and own orders (FLOW_2 §5). GET /api/me/orders/{id}/payments is L1's MePaymentsController.
[ApiController]
[Route("api/me/orders")]
[Authorize(Roles = ApiRoles.Farmer)]
public sealed class MeOrdersController(IMeOrderService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Checkout(CheckoutRequest request, CancellationToken token)
    {
        var response = await service.CheckoutAsync(request, token);
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpGet]
    public Task<PagedResult<OrderListItem>> List([FromQuery] MyOrderListRequest request, CancellationToken token) =>
        service.ListAsync(request, token);

    [HttpGet("{id:guid}")]
    public Task<OrderResponse> Get(Guid id, CancellationToken token) => service.GetAsync(id, token);

    [HttpPost("{id:guid}/cancel")]
    public Task<OrderResponse> Cancel(Guid id, CancelMyOrderRequest request, CancellationToken token) =>
        service.CancelAsync(id, request, token);
}
