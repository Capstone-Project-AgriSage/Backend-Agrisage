using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Payments;

// payOS payments (FLOW_2 §6): staff links, status sync and the payOS webhook. The webhook is anonymous and answers 200 for
// everything except a bad signature (400), so payOS retries only real failures (500). Logs carry the order code and the
// outcome only — never the payload, signature or keys.
[ApiController]
public sealed class PayOsPaymentsController(IPayOsPaymentService service, ILogger<PayOsPaymentsController> logger) : ControllerBase
{
    private const string OperateOrFarmer = $"{ApiRoles.Operate},{ApiRoles.Farmer}";

    [HttpPost("api/payments/payos")]
    [Authorize(Roles = ApiRoles.Operate)]
    public async Task<IActionResult> Create(PayOsPaymentRequest request, CancellationToken token)
    {
        var response = await service.CreateAsync(request, token);
        return Created($"/api/payments/{response.PaymentId}", response);
    }

    // Operate, or a Farmer for their own payment (others → 404).
    [HttpPost("api/payments/{id:guid}/sync")]
    [Authorize(Roles = OperateOrFarmer)]
    public Task<PaymentResponse> Sync(Guid id, CancellationToken token) => service.SyncAsync(id, token);

    // Test environment only: 404 unless the API runs with PayOS:Mode=Simulated. Operate, or a Farmer for their own payment.
    [HttpPost("api/payments/{id:guid}/simulate-paid")]
    [Authorize(Roles = OperateOrFarmer)]
    public Task<PaymentResponse> SimulatePaid(Guid id, CancellationToken token) => service.SimulatePaidAsync(id, token);

    [HttpPost("api/payments/payos/webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(CancellationToken token)
    {
        using var reader = new StreamReader(Request.Body);
        var result = await service.HandleWebhookAsync(await reader.ReadToEndAsync(token), token);

        switch (result.Outcome)
        {
            case PayOsWebhookOutcome.BadSignature:
                logger.LogWarning("payOS webhook rejected: invalid signature.");
                return BadRequest();
            case PayOsWebhookOutcome.UnknownOrderCode:
                logger.LogInformation("payOS webhook for unknown order code {OrderCode} ignored.", result.OrderCode);
                break;
            case PayOsWebhookOutcome.AmountMismatch:
                logger.LogError("payOS webhook amount differs from payment {OrderCode}; left PENDING for staff follow-up.", result.OrderCode);
                break;
            case PayOsWebhookOutcome.PaidAfterClose:
                logger.LogCritical("payOS reports money received for closed payment {OrderCode}; handle it manually.", result.OrderCode);
                break;
            case PayOsWebhookOutcome.AllocationRefused:
                logger.LogCritical("payOS payment {OrderCode} was paid but could not be applied; left PENDING for manual handling.", result.OrderCode);
                break;
            default:
                logger.LogInformation("payOS webhook {Outcome} for order code {OrderCode}.", result.Outcome, result.OrderCode);
                break;
        }

        return Ok();
    }
}
