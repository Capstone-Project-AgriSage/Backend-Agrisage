namespace AgriSage.Application.Features.Credit;

// Cross-module interface (API_CONTRACT_CUSTOMERS_CREDIT.md §6.4). Owner: group B (task B4). Used by payments (C1, C3).
// Runs inside the caller's transaction and never saves.
public interface ICreditReservationAdjuster
{
    // A payment for an already confirmed CREDIT order became PAID: release the same amount of the order's unused
    // credit reservation (database design §XVI-D).
    Task OnOrderPrepaymentAsync(Guid orderId, decimal paidAmount, Guid actorId, CancellationToken cancellationToken);
}
