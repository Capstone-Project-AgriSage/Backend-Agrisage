using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Permissions;

public sealed class PermissionConfigurationService(IAgriSageDbContext db, ICurrentUserService actor,
    PermissionEvaluator permissions, IPermissionWriteLock writeLock, AuditTrail audit) : IPermissionConfigurationService
{
    private async Task<CurrentPermissionsResponse> ManagerAsync(CancellationToken token)
    {
        var current = await permissions.CurrentAsync(token);
        if (current.Role != "ADMIN" && (current.Role != "STORE_OWNER" || !current.Permissions.Contains("PERMISSIONS.DELEGATE")))
            throw new ForbiddenException();
        return current;
    }
    private async Task AdminAsync(CancellationToken token)
    {
        if ((await permissions.CurrentAsync(token)).Role != "ADMIN") throw new ForbiddenException();
    }
    public async Task<IReadOnlyList<PermissionResponse>> CatalogAsync(CancellationToken token)
    {
        await ManagerAsync(token);
        var active = await db.Permissions.AsNoTracking().Where(p => p.IsActive).Select(p => p.Code).ToListAsync(token);
        return PermissionCatalog.Entries.Where(p => active.Contains(p.Code)).Select(p => new PermissionResponse(p.Id, p.Code,
            p.Module, p.Name, p.Delegable, p.DefaultRoles,
            new[] { "ADMIN", "STORE_OWNER", "SALES_STAFF", "DELIVERY_STAFF", "FARMER" }.Where(p.AllowsRole).ToList())).ToList();
    }
    public async Task<IReadOnlyList<RolePermissionsResponse>> RolesAsync(CancellationToken token)
    {
        await AdminAsync(token);
        var roles = await db.Roles.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.Code).ToListAsync(token);
        var result = new List<RolePermissionsResponse>();
        foreach (var role in roles) result.Add(await RoleAsync(role, token));
        return result;
    }
    private async Task<RolePermissionsResponse> RoleAsync(Role role, CancellationToken token) =>
        new(role.Id, EnumText.Format(role.Code), role.Name, role.Description, role.Version, role.Code != RoleCode.Admin,
            role.Code == RoleCode.Admin ? await db.Permissions.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Code).Select(p => p.Code).ToListAsync(token)
                : (await permissions.RoleDefaultsAsync(role.Id, token)).Order(StringComparer.Ordinal).ToList());

    public async Task<RolePermissionsResponse> SetRoleAsync(Guid roleId, SetRolePermissionsRequest request, CancellationToken token)
    {
        await using var transaction = await db.BeginTransactionAsync(token);
        await writeLock.AcquireAsync(token);
        await AdminAsync(token);
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Id == roleId && r.IsActive, token) ?? throw new NotFoundException("Role", roleId);
        if (role.Code == RoleCode.Admin) throw new ForbiddenException("Admin permissions are fixed.");
        if (role.Version != request.Version) throw new ConflictException("Role permissions changed; reload the configuration.");
        var code = EnumText.Format(role.Code);
        if (request.PermissionCodes.Any(c => !PermissionCatalog.ByCode[c].AllowsRole(code)))
            throw new ForbiddenException("A permission is outside this role's authority.");
        var catalog = await db.Permissions.AsNoTracking().Where(p => p.IsActive).ToListAsync(token);
        if (request.PermissionCodes.Any(c => catalog.All(p => p.Code != c))) throw new BusinessRuleException("A permission is inactive.");
        var existing = await db.RolePermissions.Where(p => p.RoleId == role.Id).ToListAsync(token);
        var old = catalog.Where(p => existing.Any(r => r.PermissionId == p.Id && r.IsGranted)).Select(p => p.Code).Order(StringComparer.Ordinal).ToList();
        var granted = request.PermissionCodes.ToHashSet(StringComparer.Ordinal);
        if (!old.SequenceEqual(granted.Order(StringComparer.Ordinal)))
        {
            foreach (var permission in catalog)
            {
                var row = existing.SingleOrDefault(r => r.PermissionId == permission.Id);
                if (row is null && granted.Contains(permission.Code)) db.RolePermissions.Add(new(role.Id, permission.Id, true));
                else row?.SetGrant(granted.Contains(permission.Code));
            }
            role.MarkPermissionsChanged();
            audit.Record("ROLE_PERMISSIONS_CHANGED", "ROLE", role.Id, null,
                new { permissionCodes = old }, new { permissionCodes = granted.Order(StringComparer.Ordinal).ToList() }, request.Reason.Trim());
            await db.SaveChangesAsync(token);
        }
        await transaction.CommitAsync(token);
        return await RoleAsync(role, token);
    }

    private async Task<StoreMember> TargetAsync(Guid userId, Guid store, CancellationToken token)
    {
        if (userId == actor.UserId) throw new ForbiddenException("You cannot change your own permissions.");
        return await db.StoreMembers.Include(m => m.User).ThenInclude(u => u.Role)
            .SingleOrDefaultAsync(m => m.UserId == userId && m.StoreId == store && m.Status == StoreMemberStatus.Active
                && m.User.Status == UserStatus.Active && m.User.Role.IsActive && m.User.Role.Code == RoleCode.SalesStaff, token)
            ?? throw new NotFoundException("Active Sale member", userId);
    }
    public async Task<MemberPermissionsResponse> MemberAsync(Guid userId, CancellationToken token)
    {
        var manager = await ManagerAsync(token);
        var store = manager.StoreId ?? await ActiveStore.GetIdAsync(db, token);
        return await MemberResponseAsync(await TargetAsync(userId, store, token), manager, token);
    }
    private async Task<MemberPermissionsResponse> MemberResponseAsync(StoreMember member, CurrentPermissionsResponse manager, CancellationToken token)
    {
        var overrides = await (from o in db.StoreMemberPermissions.AsNoTracking()
            join p in db.Permissions.AsNoTracking() on o.PermissionId equals p.Id
            where o.StoreMemberId == member.Id && p.IsActive orderby p.Code select new PermissionOverride(p.Code, o.IsGranted)).ToListAsync(token);
        var active = await db.Permissions.AsNoTracking().Where(p => p.IsActive && p.IsDelegable).Select(p => p.Code).ToListAsync(token);
        var effective = await permissions.ForUserAsync(member.UserId, token);
        return new(member.UserId, member.User.FullName, "SALES_STAFF", member.StoreId, member.Version, member.User.Role.Version,
            (await permissions.RoleDefaultsAsync(member.User.RoleId, token)).Order(StringComparer.Ordinal).ToList(), effective.Permissions,
            overrides, active.Where(c => manager.Permissions.Contains(c)).Order(StringComparer.Ordinal).ToList());
    }
    public async Task<MemberPermissionsResponse> SetMemberAsync(Guid userId, SetMemberPermissionsRequest request, CancellationToken token)
    {
        await using var transaction = await db.BeginTransactionAsync(token);
        await writeLock.AcquireAsync(token);
        var manager = await ManagerAsync(token);
        var store = manager.StoreId ?? await ActiveStore.GetIdAsync(db, token);
        var member = await TargetAsync(userId, store, token);
        if (member.Version != request.Version || member.User.Role.Version != request.RoleVersion)
            throw new ConflictException("Permissions changed; reload the configuration.");
        if (request.Overrides.Any(o => !PermissionCatalog.ByCode[o.Code].Delegable || !manager.Permissions.Contains(o.Code)))
            throw new ForbiddenException("A permission is outside your delegable authority.");
        var catalog = await db.Permissions.AsNoTracking().ToDictionaryAsync(p => p.Code, token);
        var rows = await db.StoreMemberPermissions.Where(p => p.StoreMemberId == member.Id).ToListAsync(token);
        var old = rows.Select(o => new { code = catalog.Values.Single(p => p.Id == o.PermissionId).Code, granted = o.IsGranted }).OrderBy(o => o.code).ToList();
        var changed = false;
        foreach (var item in request.Overrides)
        {
            if (!catalog.TryGetValue(item.Code, out var permission) || !permission.IsActive) throw new BusinessRuleException("A permission is inactive.");
            var row = rows.SingleOrDefault(o => o.PermissionId == permission.Id);
            if (row?.IsGranted == item.Granted) continue;
            if (row is null) { row = new(member.Id, permission.Id, item.Granted); rows.Add(row); db.StoreMemberPermissions.Add(row); }
            else row.SetGrant(item.Granted);
            changed = true;
        }
        if (changed)
        {
            member.MarkPermissionsChanged();
            audit.Record("STAFF_PERMISSIONS_CHANGED", "STAFF", userId, store,
                new { overrides = old }, new { overrides = rows.Select(o => new { code = catalog.Values.Single(p => p.Id == o.PermissionId).Code, granted = o.IsGranted }).OrderBy(o => o.code).ToList() }, request.Reason.Trim());
            await db.SaveChangesAsync(token);
        }
        await transaction.CommitAsync(token);
        return await MemberResponseAsync(member, manager, token);
    }
}
