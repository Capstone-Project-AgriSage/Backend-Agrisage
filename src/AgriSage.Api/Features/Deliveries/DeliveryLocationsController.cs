using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Deliveries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Deliveries;

// Live delivery location (FLOW_2 §9a, F2.8). The driver who started an IN_PROGRESS attempt sends positions; staff read
// routes and the running trips. DELIVERY_STAFF only reach the deliveries assigned to them (decision D8).
[ApiController]
[Authorize(Roles = ApiRoles.Read)]
public sealed class DeliveryLocationsController(IDeliveryLocationService service) : ControllerBase
{
    [HttpPost("api/deliveries/{id:guid}/attempts/{attemptId:guid}/locations")]
    public Task<RecordLocationsResponse> Record(Guid id, Guid attemptId, RecordLocationsRequest request, CancellationToken token) =>
        service.RecordAsync(id, attemptId, request, token);

    [HttpGet("api/deliveries/{id:guid}/attempts/{attemptId:guid}/locations")]
    public Task<IReadOnlyList<LocationPointResponse>> Route(Guid id, Guid attemptId, CancellationToken token) =>
        service.RouteAsync(id, attemptId, token);

    [HttpGet("api/deliveries/live")]
    public Task<IReadOnlyList<LiveDeliveryResponse>> Live(CancellationToken token) => service.LiveAsync(token);
}
