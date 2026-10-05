using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Returns;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Returns;

[ApiController]
[Authorize(Roles = ApiRoles.Manage)]
public sealed class RefundsController(IRefundService service) : ControllerBase
{
    [HttpPost("api/returns/{id:guid}/refunds")]
    [ProducesResponseType<RefundResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateReturn(Guid id, RefundRequest request, CancellationToken token)
    {
        var response = await service.CreateReturnAsync(id, request, token);
        return CreatedAtAction(nameof(SalesReturnsController.Get), "SalesReturns", new { id }, response);
    }
    [HttpPost("api/returns/{id:guid}/refunds/{refundId:guid}/complete")]
    public async Task<ActionResult<RefundResponse>> CompleteReturn(Guid id, Guid refundId, CompleteRefundRequest request, CancellationToken token) => await service.CompleteReturnAsync(id, refundId, request, token);
    [HttpPost("api/returns/{id:guid}/refunds/{refundId:guid}/fail")]
    public async Task<ActionResult<RefundResponse>> FailReturn(Guid id, Guid refundId, FailRefundRequest request, CancellationToken token) => await service.FailReturnAsync(id, refundId, request, token);
    [HttpPost("api/returns/{id:guid}/refunds/{refundId:guid}/cancel")]
    public async Task<ActionResult<RefundResponse>> CancelReturn(Guid id, Guid refundId, CancelRefundRequest request, CancellationToken token) => await service.CancelReturnAsync(id, refundId, request, token);
    [HttpGet("api/orders/{id:guid}/refunds")]
    public async Task<ActionResult<IReadOnlyList<RefundResponse>>> ListOrder(Guid id, CancellationToken token) => (await service.ListOrderAsync(id, token)).ToList();
    [HttpPost("api/orders/{id:guid}/refunds")]
    [ProducesResponseType<RefundResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateOrder(Guid id, OrderRefundRequest request, CancellationToken token)
    {
        var response = await service.CreateOrderAsync(id, request, token);
        return CreatedAtAction(nameof(ListOrder), new { id }, response);
    }
    [HttpPost("api/orders/{id:guid}/refunds/{refundId:guid}/complete")]
    public async Task<ActionResult<RefundResponse>> CompleteOrder(Guid id, Guid refundId, CompleteRefundRequest request, CancellationToken token) => await service.CompleteOrderAsync(id, refundId, request, token);
    [HttpPost("api/orders/{id:guid}/refunds/{refundId:guid}/fail")]
    public async Task<ActionResult<RefundResponse>> FailOrder(Guid id, Guid refundId, FailRefundRequest request, CancellationToken token) => await service.FailOrderAsync(id, refundId, request, token);
    [HttpPost("api/orders/{id:guid}/refunds/{refundId:guid}/cancel")]
    public async Task<ActionResult<RefundResponse>> CancelOrder(Guid id, Guid refundId, CancelRefundRequest request, CancellationToken token) => await service.CancelOrderAsync(id, refundId, request, token);
}
