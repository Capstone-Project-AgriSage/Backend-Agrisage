using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Deliveries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Deliveries;

// Delivery notes (FLOW_2 §7). Reads are open to every staff role; DELIVERY_STAFF only see the deliveries assigned to
// them (others → 404, decision D8). Writes are Operate.
[ApiController]
[Authorize(Roles = ApiRoles.Read)]
public sealed class DeliveriesController(IDeliveryService service) : ControllerBase
{
    [HttpPost("api/deliveries")]
    [Authorize(Roles = ApiRoles.Operate)]
    public async Task<IActionResult> Create(CreateDeliveryRequest request, CancellationToken token)
    {
        var response = await service.CreateAsync(request, token);
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpGet("api/deliveries")]
    public Task<PagedResult<DeliveryListItem>> List([FromQuery] DeliveryListRequest request, CancellationToken token) =>
        service.ListAsync(request, token);

    [HttpGet("api/deliveries/{id:guid}")]
    public Task<DeliveryResponse> Get(Guid id, CancellationToken token) => service.GetAsync(id, token);

    [HttpGet("api/orders/{id:guid}/deliveries")]
    [Authorize(Roles = ApiRoles.Operate)]
    public Task<IReadOnlyList<DeliveryListItem>> ListForOrder(Guid id, CancellationToken token) => service.ListForOrderAsync(id, token);

    [HttpPost("api/deliveries/{id:guid}/assign")]
    [Authorize(Roles = ApiRoles.Operate)]
    public Task<DeliveryResponse> Assign(Guid id, AssignDeliveryRequest request, CancellationToken token) =>
        service.AssignAsync(id, request, token);

    [HttpPut("api/deliveries/{id:guid}/items/{itemId:guid}/lots")]
    [Authorize(Roles = ApiRoles.Operate)]
    public Task<DeliveryResponse> ChangeLots(Guid id, Guid itemId, ChangeLotsRequest request, CancellationToken token) =>
        service.ChangeLotsAsync(id, itemId, request, token);

    [HttpPost("api/deliveries/{id:guid}/dispatch")]
    [Authorize(Roles = ApiRoles.Operate)]
    public Task<DeliveryResponse> Dispatch(Guid id, CancellationToken token) => service.DispatchAsync(id, token);

    [HttpPost("api/deliveries/{id:guid}/cancel")]
    [Authorize(Roles = ApiRoles.Operate)]
    public Task<DeliveryResponse> Cancel(Guid id, CancelDeliveryRequest request, CancellationToken token) =>
        service.CancelAsync(id, request, token);
}
