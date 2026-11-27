using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Deliveries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Deliveries;

// A Farmer follows the deliveries of their own order (FLOW_2 §9): no lots, costs or internal notes.
[ApiController]
[Authorize(Roles = ApiRoles.Farmer)]
public sealed class MeDeliveriesController(IMyDeliveryService service, IDeliveryLocationService locations) : ControllerBase
{
    [HttpGet("api/me/orders/{id:guid}/deliveries")]
    public Task<IReadOnlyList<MyDeliveryResponse>> List(Guid id, CancellationToken token) => service.ListForMyOrderAsync(id, token);

    // The driver's position while a trip of this delivery runs (FLOW_2 §9a); nothing once it has ended.
    [HttpGet("api/me/orders/{id:guid}/deliveries/{deliveryId:guid}/tracking")]
    public Task<MyDeliveryTrackingResponse> Tracking(Guid id, Guid deliveryId, CancellationToken token) =>
        locations.MyTrackingAsync(id, deliveryId, token);
}
