namespace AgriSage.Application.Features.Returns;

public sealed record RefundRequest(string RefundMethod, decimal Amount, Guid? OriginalPaymentId = null, string? ExternalReference = null, string? Note = null);
public sealed record OrderRefundRequest(Guid OriginalPaymentId, string RefundMethod, decimal Amount, string? Note = null);
public sealed record CompleteRefundRequest(string? ExternalReference = null, string? ProofFileUrl = null, string? Note = null);
public sealed record FailRefundRequest(string? Note = null);
public sealed record CancelRefundRequest(string Reason);

public interface IRefundService
{
    Task<RefundResponse> CreateReturnAsync(Guid id, RefundRequest request, CancellationToken token);
    Task<RefundResponse> CompleteReturnAsync(Guid id, Guid refundId, CompleteRefundRequest request, CancellationToken token);
    Task<RefundResponse> FailReturnAsync(Guid id, Guid refundId, FailRefundRequest request, CancellationToken token);
    Task<RefundResponse> CancelReturnAsync(Guid id, Guid refundId, CancelRefundRequest request, CancellationToken token);
    Task<IReadOnlyList<RefundResponse>> ListOrderAsync(Guid id, CancellationToken token);
    Task<RefundResponse> CreateOrderAsync(Guid id, OrderRefundRequest request, CancellationToken token);
    Task<RefundResponse> CompleteOrderAsync(Guid id, Guid refundId, CompleteRefundRequest request, CancellationToken token);
    Task<RefundResponse> FailOrderAsync(Guid id, Guid refundId, FailRefundRequest request, CancellationToken token);
    Task<RefundResponse> CancelOrderAsync(Guid id, Guid refundId, CancelRefundRequest request, CancellationToken token);
}
