using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Payments;

// A Farmer pays online with payOS (FLOW_2 §6): their own orders and debt only; others' ids are "not found".
[ApiController]
[Authorize(Roles = ApiRoles.Farmer)]
public sealed class MePayOsPaymentsController(IPayOsPaymentService service) : ControllerBase
{
    [HttpPost("api/me/payments/payos")]
    public async Task<IActionResult> Create(PayOsPaymentRequest request, CancellationToken token)
    {
        var response = await service.CreateAsync(request, token);
        return Created($"/api/me/payments/{response.PaymentId}", response);
    }

    [HttpPost("api/me/payments/{id:guid}/cancel")]
    public Task<PaymentResponse> Cancel(Guid id, CancellationToken token) => service.CancelMineAsync(id, token);
}
