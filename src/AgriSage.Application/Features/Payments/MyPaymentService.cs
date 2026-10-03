using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;

namespace AgriSage.Application.Features.Payments;

public interface IMyPaymentService
{
    Task<PagedResult<PaymentListItem>> ListAsync(MyPaymentListRequest request, CancellationToken cancellationToken);

    Task<PaymentResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<OrderPaymentSummary> GetOrderSummaryAsync(Guid orderId, CancellationToken cancellationToken);
}

// A Farmer's own payments (FLOW_1 §5, /api/me/...): the Farmer is the signed-in user's profile, never a client value;
// someone else's payment or order is "not found".
public sealed class MyPaymentService(PaymentQueries queries, ICurrentUserService currentUser) : IMyPaymentService
{
    public async Task<PagedResult<PaymentListItem>> ListAsync(MyPaymentListRequest request, CancellationToken cancellationToken)
    {
        var farmerId = await FarmerAsync(cancellationToken);

        return await queries.ListAsync(
            new PaymentListRequest { Status = request.Status, Page = request.Page, PageSize = request.PageSize },
            farmerId,
            cancellationToken);
    }

    public async Task<PaymentResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await queries.GetAsync(id, await FarmerAsync(cancellationToken), cancellationToken);

    public async Task<OrderPaymentSummary> GetOrderSummaryAsync(Guid orderId, CancellationToken cancellationToken) =>
        await queries.GetOrderSummaryAsync(orderId, await FarmerAsync(cancellationToken), cancellationToken);

    private async Task<Guid> FarmerAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");

        return await queries.FindFarmerProfileIdAsync(userId, cancellationToken)
            ?? throw new ForbiddenException("This account has no customer profile.");
    }
}
