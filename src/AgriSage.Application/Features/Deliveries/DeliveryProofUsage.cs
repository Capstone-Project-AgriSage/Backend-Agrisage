using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Files;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Deliveries;

// A photo saved on a delivery attempt, incident, or refund — including soft-deleted rows, which keep their
// history — cannot be deleted from storage (decision C-D3). Saved URLs may end with the storage key, query or fragment.
public sealed class DeliveryProofUsage(IAgriSageDbContext context) : IProofPhotoUsage
{
    public async Task<bool> IsUsedAsync(string storageKey, CancellationToken cancellationToken)
    {
        var suffix = "/" + storageKey;
        var query = suffix + "?";
        var fragment = suffix + "#";

        return await context.Refunds.IgnoreQueryFilters().AsNoTracking()
                   .AnyAsync(r => r.ProofFileUrl != null
                       && (r.ProofFileUrl.EndsWith(suffix) || r.ProofFileUrl.Contains(query) || r.ProofFileUrl.Contains(fragment)),
                       cancellationToken)
               || await context.DeliveryAttempts.IgnoreQueryFilters().AsNoTracking()
                   .AnyAsync(a => a.ProofImageUrl != null
                       && (a.ProofImageUrl.EndsWith(suffix) || a.ProofImageUrl.Contains(query) || a.ProofImageUrl.Contains(fragment)),
                       cancellationToken)
               || await context.DeliveryIncidents.IgnoreQueryFilters().AsNoTracking()
                   .AnyAsync(i => i.EvidenceImageUrl != null
                       && (i.EvidenceImageUrl.EndsWith(suffix) || i.EvidenceImageUrl.Contains(query) || i.EvidenceImageUrl.Contains(fragment)),
                       cancellationToken);
    }
}
