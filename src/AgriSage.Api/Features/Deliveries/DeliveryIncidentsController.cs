using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Deliveries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Deliveries;

// Delivery incidents (FLOW_2 §8): the assigned DELIVERY_STAFF member or Operate report them; only Operate resolves one.
[ApiController]
[Route("api/deliveries/{id:guid}/incidents")]
[Authorize(Roles = ApiRoles.Read)]
public sealed class DeliveryIncidentsController(IDeliveryIncidentService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report(Guid id, ReportIncidentRequest request, CancellationToken token)
    {
        var response = await service.ReportAsync(id, request, token);
        return Created($"/api/deliveries/{id}/incidents", response);
    }

    [HttpGet]
    public Task<IReadOnlyList<DeliveryIncidentResponse>> List(Guid id, CancellationToken token) => service.ListAsync(id, token);

    [HttpPost("{incidentId:guid}/resolve")]
    [Authorize(Roles = ApiRoles.Operate)]
    public Task<DeliveryIncidentResponse> Resolve(Guid id, Guid incidentId, ResolveIncidentRequest request, CancellationToken token) =>
        service.ResolveAsync(id, incidentId, request, token);
}
