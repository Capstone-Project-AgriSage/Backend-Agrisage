using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth;
using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Credit.Enums;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Customers;

// Shared steps: only stage changes. The caller owns the transaction, SaveChanges and commit.
public sealed class CustomerWrites(IAgriSageDbContext context, ICurrentUserService currentUser,
    IDateTimeProvider clock, AuditTrail audit)
{
    public Guid Actor(bool manage = false)
    {
        var id = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        if (!RoleCodeFormat.TryParse(currentUser.Role, out var role)
            || (role != RoleCode.Admin && role != RoleCode.StoreOwner && (manage || role != RoleCode.SalesStaff)))
        {
            throw new ForbiddenException();
        }

        return id;
    }

    public async Task<Guid> OwnCustomerAsync(CancellationToken token)
    {
        var id = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        return await context.FarmerProfiles.AsNoTracking().Where(f => f.UserId == id && f.User.Role.Code == RoleCode.Farmer)
            .Select(f => (Guid?)f.Id).SingleOrDefaultAsync(token) ?? throw new ForbiddenException();
    }

    public async Task<FarmerProfile> FindAsync(Guid id, CancellationToken token) =>
        await context.FarmerProfiles.Include(f => f.User).ThenInclude(u => u.Role)
            .FirstOrDefaultAsync(f => f.Id == id && f.User.Role.Code == RoleCode.Farmer, token)
        ?? throw new NotFoundException("Customer", id);

    public async Task AssignAsync(Guid farmerId, Guid storeId, Guid groupId, string? reason, CancellationToken token)
    {
        var group = await context.CustomerGroups.FirstOrDefaultAsync(g => g.Id == groupId && g.StoreId == storeId, token)
            ?? throw new NotFoundException("Customer group", groupId);
        if (!group.IsActive)
        {
            throw new BusinessRuleException("An inactive customer group cannot be assigned.");
        }

        var current = await context.CustomerGroupAssignments
            .FirstOrDefaultAsync(a => a.FarmerProfileId == farmerId && a.EffectiveTo == null, token);
        if (current?.CustomerGroupId == groupId)
        {
            return;
        }
        if (current != null && current.EffectiveFrom >= clock.UtcNow)
        {
            throw new BusinessRuleException("A new group assignment must start after the current assignment.");
        }

        current?.End(clock.UtcNow);
        context.CustomerGroupAssignments.Add(new CustomerGroupAssignment(farmerId, groupId, clock.UtcNow, Actor(), Texts.Clean(reason)));
        var profile = await context.FarmerCreditProfiles
            .FirstOrDefaultAsync(p => p.FarmerProfileId == farmerId && p.StoreId == storeId, token);
        if (profile != null && group.DefaultCreditTierId is { } tierId && profile.CreditTierId != tierId)
        {
            await RequireTierAsync(tierId, storeId, token);
            ChangeLimit(profile, profile.CreditLimit, tierId, $"Customer group changed to {group.Code}");
        }
        audit.Record("CUSTOMER_GROUP_ASSIGNED", "CUSTOMER", farmerId, storeId,
            newValues: new { CustomerGroupId = groupId }, reason: reason);
    }

    public async Task ApplyCreditAsync(Guid farmerId, Guid storeId, CustomerRequest request, CancellationToken token)
    {
        if (request.AllowCreditPurchase is null && request.CreditLimit is null && request.CreditTierId is null)
        {
            return;
        }
        var profile = await context.FarmerCreditProfiles
            .FirstOrDefaultAsync(p => p.StoreId == storeId && p.FarmerProfileId == farmerId, token);
        if (profile == null)
        {
            if (request.AllowCreditPurchase == false)
            {
                return;
            }
            await CreateCreditAsync(farmerId, storeId, request.CreditTierId, request.CreditLimit, request.Notes, token);
            return;
        }

        var limit = request.CreditLimit ?? profile.CreditLimit;
        var tier = request.CreditTierId ?? profile.CreditTierId;
        if (tier != profile.CreditTierId && tier is { } tierId)
        {
            await RequireTierAsync(tierId, storeId, token);
        }
        if (limit != profile.CreditLimit || tier != profile.CreditTierId)
        {
            // FLOW_3 §4.3 explicitly allows a limit below current exposure; future credit is then blocked.
            ChangeLimit(profile, limit, tier, request.CreditChangeReason ?? "");
        }
        if (request.AllowCreditPurchase == false && profile.Status == FarmerCreditProfileStatus.Active)
        {
            SetCreditStatus(profile, FarmerCreditProfileStatus.Suspended, request.CreditChangeReason ?? "");
        }
        else if (request.AllowCreditPurchase == true && profile.Status != FarmerCreditProfileStatus.Active)
        {
            SetCreditStatus(profile, FarmerCreditProfileStatus.Active, request.CreditChangeReason ?? "");
        }
    }

    public async Task<FarmerCreditProfile> CreateCreditAsync(Guid farmerId, Guid storeId, Guid? tierId,
        decimal? limit, string? note, CancellationToken token)
    {
        if (await context.FarmerCreditProfiles.AnyAsync(p => p.StoreId == storeId && p.FarmerProfileId == farmerId, token))
        {
            throw new ConflictException("This customer already has a credit profile.");
        }
        var groupTier = await context.CustomerGroupAssignments
            .Where(a => a.FarmerProfileId == farmerId && a.EffectiveTo == null && a.CustomerGroup.StoreId == storeId)
            .Select(a => a.CustomerGroup.DefaultCreditTierId).FirstOrDefaultAsync(token);
        // Include an assignment staged in the same unit of work (new customer / updated group).
        var staged = context.CustomerGroupAssignments.Local.FirstOrDefault(a => a.FarmerProfileId == farmerId && a.EffectiveTo == null);
        if (staged != null)
        {
            groupTier = await context.CustomerGroups.Where(g => g.Id == staged.CustomerGroupId && g.StoreId == storeId)
                .Select(g => g.DefaultCreditTierId).FirstOrDefaultAsync(token);
        }
        tierId ??= groupTier ?? await context.CustomerGroups.Where(g => g.StoreId == storeId && g.IsActive && g.IsDefault)
            .Select(g => g.DefaultCreditTierId).FirstOrDefaultAsync(token);
        if (tierId is null)
        {
            throw new BusinessRuleException("Choose a credit tier for this customer.");
        }
        var tier = await RequireTierAsync(tierId.Value, storeId, token);
        var profile = new FarmerCreditProfile(storeId, farmerId, limit ?? tier.DefaultCreditLimit, Actor(), clock.UtcNow, tier.Id, Texts.Clean(note));
        context.FarmerCreditProfiles.Add(profile);
        if (!await context.DebtAccounts.AnyAsync(a => a.StoreId == storeId && a.FarmerProfileId == farmerId, token))
        {
            context.DebtAccounts.Add(new DebtAccount(storeId, farmerId));
        }
        audit.Record("CUSTOMER_CREDIT_CREATED", "CREDIT_PROFILE", profile.Id, storeId,
            newValues: new { profile.CreditLimit, profile.CreditTierId });
        return profile;
    }

    public Task<CreditTier> RequireTierAsync(Guid id, Guid storeId, CancellationToken token) =>
        FindTierAsync(id, storeId, token);

    private async Task<CreditTier> FindTierAsync(Guid id, Guid storeId, CancellationToken token) =>
        await context.CreditTiers.FirstOrDefaultAsync(t => t.Id == id && t.StoreId == storeId && t.IsActive, token)
        ?? throw new BusinessRuleException("Credit tier must be active and belong to the active store.");

    public void ChangeLimit(FarmerCreditProfile profile, decimal limit, Guid? tierId, string reason)
    {
        RequireReason(reason);
        var before = new { profile.CreditLimit, profile.CreditTierId };
        var history = profile.ChangeCreditLimit(limit, tierId, reason.Trim(), Actor(), clock.UtcNow);
        // Track a child of an existing aggregate explicitly, like the existing Orders feature.
        context.CreditLimitHistories.Add(history);
        audit.Record("CUSTOMER_CREDIT_LIMIT_CHANGED", "CREDIT_PROFILE", profile.Id, profile.StoreId,
            before, new { profile.CreditLimit, profile.CreditTierId }, reason);
    }

    public void SetCreditStatus(FarmerCreditProfile profile, FarmerCreditProfileStatus status, string reason)
    {
        Actor(manage: true); // credit status changes are Manage, even when submitted through the customer form.
        RequireReason(reason);
        var before = new { Status = EnumText.Format(profile.Status) };
        profile.ChangeStatus(status);
        audit.Record("CUSTOMER_CREDIT_STATUS_CHANGED", "CREDIT_PROFILE", profile.Id, profile.StoreId,
            before, new { Status = EnumText.Format(status) }, reason);
    }

    private static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("A reason is required for a credit change.");
        }
    }
}
