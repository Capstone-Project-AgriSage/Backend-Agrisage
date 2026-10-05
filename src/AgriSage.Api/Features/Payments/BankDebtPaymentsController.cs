using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Payments;

[ApiController]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class BankDebtPaymentsController(IBankDebtPaymentService service) : ControllerBase
{
    [HttpPost("api/payments/bank-transfer")]
    public async Task<IActionResult> Record(BankDebtPaymentRequest request, CancellationToken token)
    {
        var response = await service.RecordAsync(request, token);
        return Created($"/api/payments/{response.Id}", response);
    }
    [HttpPost("api/payments/{id:guid}/confirm")]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<PaymentResponse> Confirm(Guid id, CancellationToken token) => service.ConfirmAsync(id, token);
    [HttpPost("api/payments/{id:guid}/reject")]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<PaymentResponse> Reject(Guid id, RejectDebtPaymentRequest request, CancellationToken token) => service.RejectAsync(id, request, token);
}
