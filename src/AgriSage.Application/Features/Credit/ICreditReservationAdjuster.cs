namespace AgriSage.Application.Features.Credit;

// Cross-flow interface (docs/reference/api-flows/README.md §4.5). Owner: task F3.3. Used by payments (F1.3, F2.4).
// Runs inside the caller's transaction and never saves.
public interface ICreditReservationAdjuster
{
    // A payment for an already confirmed CREDIT order became PAID: release the same amount of the order's unused
    // credit reservation (database design §XVI-D).
    Task OnOrderPrepaymentAsync(Guid orderId, decimal paidAmount, Guid actorId, CancellationToken cancellationToken);
}
