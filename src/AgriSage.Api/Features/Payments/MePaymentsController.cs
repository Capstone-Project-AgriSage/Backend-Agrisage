using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Payments;

// A Farmer's own payments (FLOW_1 §5): the Farmer is the signed-in user; other people's data is "not found".
[ApiController]
[Authorize(Roles = ApiRoles.Farmer)]
public sealed class MePaymentsController(IMyPaymentService service) : ControllerBase
{
    [HttpGet("api/me/payments")]
    public async Task<ActionResult<PagedResult<PaymentListItem>>> List(
        [FromQuery] MyPaymentListRequest request, CancellationToken cancellationToken) =>
        await service.ListAsync(request, cancellationToken);

    [HttpGet("api/me/payments/{id:guid}")]
    public async Task<ActionResult<PaymentResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await service.GetAsync(id, cancellationToken);

    [HttpGet("api/me/orders/{id:guid}/payments")]
    public async Task<ActionResult<OrderPaymentSummary>> GetOrderPayments(Guid id, CancellationToken cancellationToken) =>
        await service.GetOrderSummaryAsync(id, cancellationToken);
}
