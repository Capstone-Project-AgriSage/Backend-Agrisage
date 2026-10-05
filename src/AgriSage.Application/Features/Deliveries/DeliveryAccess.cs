using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth;
using AgriSage.Domain.Features.Deliveries.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Stores.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Deliveries;

// Who acts on deliveries (decision D8): DELIVERY_STAFF only reach the deliveries assigned to them — anything else is
// "not found", like someone else's data; Admin, Store Owner and Sales reach every delivery of the active store.
public sealed class DeliveryAccess(IAgriSageDbContext context, ICurrentUserService currentUser)
{
    public sealed record Actor(Guid UserId, RoleCode Role, Guid StoreId)
    {
        public bool IsDeliveryStaff => Role == RoleCode.DeliveryStaff;
    }

    public async Task<Actor> ActorAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        if (!RoleCodeFormat.TryParse(currentUser.Role, out var role) || role == RoleCode.Farmer)
        {
            throw new ForbiddenException();
        }

        return new Actor(userId, role, await ActiveStore.GetIdAsync(context, cancellationToken));
    }

    // Operate-only actions (create, assign, lots, dispatch, cancel, resolve) are role-gated by the API; this keeps the
    // rule when a service is called directly.
    public async Task<Actor> OperatorAsync(CancellationToken cancellationToken)
    {
        var actor = await ActorAsync(cancellationToken);

        return actor.IsDeliveryStaff ? throw new ForbiddenException() : actor;
    }

    // Deliveries the actor may see: every delivery of the store, or only those assigned to a delivery staff member.
    public IQueryable<Delivery> Scope(Actor actor) =>
        actor.IsDeliveryStaff
            ? context.Deliveries.Where(d => d.StoreId == actor.StoreId && d.AssignedToMember != null
                && d.AssignedToMember.UserId == actor.UserId)
            : context.Deliveries.Where(d => d.StoreId == actor.StoreId);

    public async Task EnsureVisibleAsync(Actor actor, Guid deliveryId, CancellationToken cancellationToken)
    {
        if (!await Scope(actor).AsNoTracking().AnyAsync(d => d.Id == deliveryId, cancellationToken))
        {
            throw new NotFoundException("Delivery", deliveryId);
        }
    }

    // The actor's ACTIVE membership of the store: attempts are recorded against a store member (§35.5). An Admin is not
    // a store member, so an Admin cannot carry out a delivery attempt.
    public async Task<Guid> MemberIdAsync(Actor actor, CancellationToken cancellationToken) =>
        await context.StoreMembers.AsNoTracking()
            .Where(m => m.StoreId == actor.StoreId && m.UserId == actor.UserId && m.Status == StoreMemberStatus.Active)
            .Select(m => (Guid?)m.Id).FirstOrDefaultAsync(cancellationToken)
        ?? throw new BusinessRuleException("Only an active member of the store can carry out a delivery attempt.");
}
