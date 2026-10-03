using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Payments;

// Cash payments and payment queries for staff (FLOW_1 §5): Admin, Store Owner and Sales.
[ApiController]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class PaymentsController(IPaymentService service) : ControllerBase
{
    // Cash is received by the caller: the payment is created PAID and allocated in one transaction.
    [HttpPost("api/payments/cash")]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> ReceiveCash(CashPaymentRequest request, CancellationToken cancellationToken)
    {
        var response = await service.ReceiveCashAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpGet("api/payments")]
    public async Task<ActionResult<PagedResult<PaymentListItem>>> List(
        [FromQuery] PaymentListRequest request, CancellationToken cancellationToken) =>
        await service.ListAsync(request, cancellationToken);

    [HttpGet("api/payments/{id:guid}")]
    public async Task<ActionResult<PaymentResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await service.GetAsync(id, cancellationToken);

    [HttpGet("api/orders/{id:guid}/payments")]
    public async Task<ActionResult<OrderPaymentSummary>> GetOrderPayments(Guid id, CancellationToken cancellationToken) =>
        await service.GetOrderSummaryAsync(id, cancellationToken);

    // Only a PENDING payment (a payOS payment is cancelled through payOS).
    [HttpPost("api/payments/{id:guid}/cancel")]
    public async Task<ActionResult<PaymentResponse>> Cancel(
        Guid id, CancelPaymentRequest request, CancellationToken cancellationToken) =>
        await service.CancelAsync(id, request, cancellationToken);
}
