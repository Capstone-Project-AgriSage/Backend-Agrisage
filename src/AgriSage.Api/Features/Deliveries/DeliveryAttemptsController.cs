using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Deliveries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Deliveries;

// Delivery attempts (FLOW_2 §8): the assigned DELIVERY_STAFF member or Operate start and complete them (the service
// scopes delivery staff to their own deliveries); only Operate cancels one.
[ApiController]
[Route("api/deliveries/{id:guid}/attempts")]
[Authorize(Roles = ApiRoles.Read)]
public sealed class DeliveryAttemptsController(IDeliveryAttemptService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Start(Guid id, StartAttemptRequest request, CancellationToken token)
    {
        var response = await service.StartAsync(id, request, token);
        return Created($"/api/deliveries/{id}/attempts", response);
    }

    [HttpGet]
    public Task<IReadOnlyList<DeliveryAttemptResponse>> List(Guid id, CancellationToken token) => service.ListAsync(id, token);

    [HttpPost("{attemptId:guid}/complete")]
    public Task<DeliveryResponse> Complete(Guid id, Guid attemptId, CompleteAttemptRequest request, CancellationToken token) =>
        service.CompleteAsync(id, attemptId, request, token);

    [HttpPost("{attemptId:guid}/cancel")]
    [Authorize(Roles = ApiRoles.Operate)]
    public Task<DeliveryResponse> Cancel(Guid id, Guid attemptId, CancelAttemptRequest request, CancellationToken token) =>
        service.CancelAsync(id, attemptId, request, token);
}
