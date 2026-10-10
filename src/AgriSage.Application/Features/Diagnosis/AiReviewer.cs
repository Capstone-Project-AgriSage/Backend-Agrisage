using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Stores.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Diagnosis;

// Who may decide a diagnosis case (business rule 41: "AI reviewer = can_review_ai, not a primary role"; AI_DIAGNOSIS.md D7).
// The endpoint's permission code only opens the route; the member's can_review_ai flag decides. The reviewer is a store
// member of the active store: agent_reviews.reviewer_member_id references store_members.
public sealed class AiReviewer(IAgriSageDbContext context, ICurrentUserService currentUser)
{
    public sealed record Member(Guid MemberId, Guid UserId, string FullName);

    public async Task<Member> EnsureAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);

        return await context.StoreMembers.AsNoTracking()
                .Where(m => m.StoreId == storeId && m.UserId == userId
                    && m.Status == StoreMemberStatus.Active && m.CanReviewAi && m.User.Status == Domain.Features.Identity.Enums.UserStatus.Active)
                .Select(m => new Member(m.Id, m.UserId, m.User.FullName))
                .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ForbiddenException("You are not allowed to review AI diagnoses.");
    }
}
