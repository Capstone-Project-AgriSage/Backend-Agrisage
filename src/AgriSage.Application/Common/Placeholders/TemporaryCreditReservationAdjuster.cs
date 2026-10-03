using AgriSage.Application.Features.Credit;

namespace AgriSage.Application.Common.Placeholders;

// TEMPORARY until task F3.3 registers the real ICreditReservationAdjuster (then delete this file and its registration).
// No credit reservations exist yet (CREDIT orders cannot be confirmed), so there is nothing to release.
public sealed class TemporaryCreditReservationAdjuster : ICreditReservationAdjuster
{
    public Task OnOrderPrepaymentAsync(Guid orderId, decimal paidAmount, Guid actorId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
