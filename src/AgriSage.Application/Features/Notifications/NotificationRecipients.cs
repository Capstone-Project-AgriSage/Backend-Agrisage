using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Stores.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Notifications;

internal static class NotificationRecipients
{
    public static Task<List<Guid>> StaffAsync(IAgriSageDbContext context, Guid storeId, CancellationToken token,
        params RoleCode[] roles) => StaffAsync(context, storeId, null, token, roles);

    // memberBefore: only members added before that moment (a notice about an earlier day goes to those who were there).
    public static Task<List<Guid>> StaffAsync(IAgriSageDbContext context, Guid storeId, DateTimeOffset? memberBefore,
        CancellationToken token, params RoleCode[] roles) => context.StoreMembers.AsNoTracking().Where(m => m.StoreId == storeId
            && m.Status == StoreMemberStatus.Active && m.User.Status == UserStatus.Active
            && m.User.Role.IsActive && m.User.Role.DeletedAt == null && roles.Contains(m.User.Role.Code)
            && (memberBefore == null || m.CreatedAt < memberBefore))
            .Select(m => m.UserId).Distinct().ToListAsync(token);

    // Active owners and Sale members of the Store whose effective permissions include the code: role defaults, a Sale
    // member's own grant or revocation, and the Owner's defaults as the Sale ceiling (the rule of PermissionEvaluator and
    // RealtimeAudience). Admins are not Store members and are not included.
    public static Task<List<Guid>> WithPermissionAsync(IAgriSageDbContext context, Guid storeId, string code,
        CancellationToken token)
    {
        var permission = context.Permissions.AsNoTracking().Where(p => p.Code == code && p.IsActive).Select(p => p.Id);
        var ownerRoles = context.Roles.AsNoTracking().Where(r => r.Code == RoleCode.StoreOwner && r.IsActive).Select(r => r.Id);
        var ownerGrants = context.RolePermissions.AsNoTracking()
            .Where(r => r.IsGranted && permission.Contains(r.PermissionId) && ownerRoles.Contains(r.RoleId));
        return context.StoreMembers.AsNoTracking().Where(m => m.StoreId == storeId
                && m.Status == StoreMemberStatus.Active && m.User.Status == UserStatus.Active
                && m.User.Role.IsActive && m.User.Role.DeletedAt == null
                && (m.User.Role.Code == RoleCode.StoreOwner || (m.User.Role.Code == RoleCode.SalesStaff && ownerGrants.Any()))
                && ((m.User.Role.Code == RoleCode.SalesStaff && context.StoreMemberPermissions.Any(o => o.StoreMemberId == m.Id
                        && permission.Contains(o.PermissionId) && o.IsGranted == true))
                    || (!(m.User.Role.Code == RoleCode.SalesStaff && context.StoreMemberPermissions.Any(o => o.StoreMemberId == m.Id
                            && permission.Contains(o.PermissionId) && o.IsGranted == false))
                        && context.RolePermissions.Any(r => r.RoleId == m.User.RoleId && permission.Contains(r.PermissionId) && r.IsGranted))))
            .Select(m => m.UserId).Distinct().ToListAsync(token);
    }
}
