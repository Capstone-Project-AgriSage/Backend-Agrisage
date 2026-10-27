using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Stores.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Notifications;

// Authorization is evaluated from current DB rows on fan-out, never from cached hub groups/JWT roles.
public sealed class RealtimeAudience(IAgriSageDbContext db)
{
    public async Task<List<Guid>> RecipientsAsync(RealtimeSignal signal, CancellationToken token)
    {
        if (signal.Topic == "permissions")
        {
            var users = db.Users.IgnoreQueryFilters().AsNoTracking();
            return signal.Scope switch
            {
                "user" => [signal.SubjectId],
                "member" => await db.StoreMembers.IgnoreQueryFilters().AsNoTracking()
                    .Where(m => m.Id == signal.SubjectId).Select(m => m.UserId).ToListAsync(token),
                // Owner defaults also cap every Sale member's effective permissions.
                "role" => await users.Where(u => u.RoleId == signal.SubjectId
                    || (u.Role.Code == RoleCode.SalesStaff && db.Roles.IgnoreQueryFilters()
                        .Any(r => r.Id == signal.SubjectId && r.Code == RoleCode.StoreOwner)))
                    .Select(u => u.Id).ToListAsync(token),
                "store" => await db.StoreMembers.IgnoreQueryFilters().AsNoTracking()
                    .Where(m => m.StoreId == signal.SubjectId).Select(m => m.UserId).Distinct().ToListAsync(token),
                _ => await users.Select(u => u.Id).ToListAsync(token)
            };
        }
        if (signal.Topic == "notifications") return [signal.SubjectId];

        Guid store;
        Guid? farmerUser = null;
        if (signal.Topic == "diagnosis")
        {
            farmerUser = await db.DiagnosisCases.AsNoTracking().Where(c => c.Id == signal.SubjectId)
                .Select(c => (Guid?)c.FarmerProfile.UserId).SingleOrDefaultAsync(token);
            store = await ActiveStore.GetIdAsync(db, token);
        }
        else if (signal.Topic == "inventory") store = signal.SubjectId;
        else return [];

        if (!await db.Stores.AsNoTracking().AnyAsync(s => s.Id == store
            && s.Status == StoreStatus.Active, token)) return [];

        var code = signal.Topic == "inventory" ? "INVENTORY.READ" : "DIAGNOSIS.READ";
        var permission = db.Permissions.AsNoTracking().Where(p => p.Code == code && p.IsActive).Select(p => p.Id);
        var ownerRoles = db.Roles.AsNoTracking().Where(r => r.Code == RoleCode.StoreOwner && r.IsActive).Select(r => r.Id);
        var ownerGrants = db.RolePermissions.AsNoTracking().Where(r => r.IsGranted && permission.Contains(r.PermissionId)
            && ownerRoles.Contains(r.RoleId));
        var members = db.StoreMembers.AsNoTracking().Where(m => m.StoreId == store && m.Status == StoreMemberStatus.Active);
        // One SQL query for the entire audience, including current defaults, overrides and the Owner ceiling.
        var recipients = await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active && u.Role.IsActive
            && u.Role.DeletedAt == null && (u.Role.Code == RoleCode.Admin
                || ((u.Role.Code == RoleCode.StoreOwner || u.Role.Code == RoleCode.SalesStaff)
                    && members.Any(m => m.UserId == u.Id
                        && (u.Role.Code != RoleCode.SalesStaff || ownerGrants.Any())
                        && ((u.Role.Code == RoleCode.SalesStaff && db.StoreMemberPermissions.Any(o => o.StoreMemberId == m.Id && permission.Contains(o.PermissionId) && o.IsGranted == true))
                            || (!(u.Role.Code == RoleCode.SalesStaff && db.StoreMemberPermissions.Any(o => o.StoreMemberId == m.Id && permission.Contains(o.PermissionId) && o.IsGranted == false))
                                && db.RolePermissions.Any(r => r.RoleId == u.RoleId && permission.Contains(r.PermissionId) && r.IsGranted)))))))
            .Select(u => u.Id).ToListAsync(token);
        if (farmerUser is { } farmer && await db.Users.AsNoTracking().AnyAsync(u => u.Id == farmer
            && u.Status == UserStatus.Active && u.Role.IsActive && u.Role.Code == RoleCode.Farmer
            && db.RolePermissions.Any(r => r.RoleId == u.RoleId && r.IsGranted
                && db.Permissions.Any(p => p.Id == r.PermissionId && p.IsActive && p.Code == "MY_DIAGNOSIS.READ")), token)) recipients.Add(farmer);
        return recipients;
    }
}
