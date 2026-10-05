using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Deliveries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Deliveries;

// A Farmer follows the deliveries of their own order (FLOW_2 §9): no lots, costs or internal notes.
[ApiController]
[Authorize(Roles = ApiRoles.Farmer)]
public sealed class MeDeliveriesController(IMyDeliveryService service) : ControllerBase
{
    [HttpGet("api/me/orders/{id:guid}/deliveries")]
    public Task<IReadOnlyList<MyDeliveryResponse>> List(Guid id, CancellationToken token) => service.ListForMyOrderAsync(id, token);
}
