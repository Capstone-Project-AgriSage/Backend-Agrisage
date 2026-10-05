using AgriSage.Application.Features.Credit;

namespace AgriSage.Tests.TestDoubles;

// Test-only no-op when the test fixture contains no credit reservations.
public sealed class StubCreditReservationAdjuster : ICreditReservationAdjuster
{
    public Task OnOrderPrepaymentAsync(Guid orderId, decimal paidAmount, Guid actorId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
