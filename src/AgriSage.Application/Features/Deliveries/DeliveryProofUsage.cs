using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Files;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Deliveries;

// A photo saved on a delivery attempt (proof) or incident (evidence) — including soft-deleted rows, which keep their
// history — cannot be deleted from storage (decision C-D3). Saved URLs end with the storage key.
public sealed class DeliveryProofUsage(IAgriSageDbContext context) : IProofPhotoUsage
{
    public async Task<bool> IsUsedAsync(string storageKey, CancellationToken cancellationToken)
    {
        var suffix = "/" + storageKey;

        return await context.DeliveryAttempts.IgnoreQueryFilters().AsNoTracking()
                   .AnyAsync(a => a.ProofImageUrl != null && a.ProofImageUrl.EndsWith(suffix), cancellationToken)
               || await context.DeliveryIncidents.IgnoreQueryFilters().AsNoTracking()
                   .AnyAsync(i => i.EvidenceImageUrl != null && i.EvidenceImageUrl.EndsWith(suffix), cancellationToken);
    }
}
