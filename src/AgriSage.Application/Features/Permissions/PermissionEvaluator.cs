using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Stores.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Permissions;

public sealed class PermissionEvaluator(IAgriSageDbContext db, ICurrentUserService actor) : IPermissionEvaluator
{
    public Task<CurrentPermissionsResponse> CurrentAsync(CancellationToken token) =>
        ForUserAsync(actor.UserId ?? throw new AuthenticationFailedException("Authentication is required."), token);
    public async Task<bool> HasAsync(string code, CancellationToken token)
    {
        if (!PermissionCatalog.ByCode.ContainsKey(code) || actor.UserId is null) return false;
        try { return (await CurrentAsync(token)).Permissions.Contains(code, StringComparer.Ordinal); }
        catch (ForbiddenException) { return false; }
    }

    internal async Task<CurrentPermissionsResponse> ForUserAsync(Guid userId, CancellationToken token)
    {
        var user = await db.Users.AsNoTracking().Where(u => u.Id == userId && u.Status == UserStatus.Active && u.Role.IsActive)
            .Select(u => new { u.RoleId, u.Role.Code, u.Role.Version }).SingleOrDefaultAsync(token) ?? throw new ForbiddenException();
        var role = EnumText.Format(user.Code);
        var active = await db.Permissions.AsNoTracking().Where(p => p.IsActive).Select(p => new { p.Id, p.Code }).ToListAsync(token);
        if (role == "ADMIN") return new(role, null, user.Version, null, active.Select(p => p.Code).Order(StringComparer.Ordinal).ToList());
        Guid? store = null;
        long? memberVersion = null;
        var granted = (await RoleDefaultsAsync(user.RoleId, token)).ToHashSet(StringComparer.Ordinal);
        if (role != "FARMER")
        {
            store = await ActiveStore.GetIdAsync(db, token);
            var member = await db.StoreMembers.AsNoTracking().Where(m => m.StoreId == store && m.UserId == userId && m.Status == StoreMemberStatus.Active)
                .Select(m => new { m.Id, m.Version }).SingleOrDefaultAsync(token) ?? throw new ForbiddenException();
            memberVersion = member.Version;
            if (role == "SALES_STAFF")
            {
                var overrides = await (from o in db.StoreMemberPermissions.AsNoTracking()
                    join p in db.Permissions.AsNoTracking() on o.PermissionId equals p.Id
                    where o.StoreMemberId == member.Id && p.IsActive select new { p.Code, o.IsGranted }).ToListAsync(token);
                foreach (var item in overrides)
                    if (item.IsGranted) granted.Add(item.Code); else granted.Remove(item.Code);
                var ownerRole = await db.Roles.AsNoTracking().Where(r => r.Code == RoleCode.StoreOwner && r.IsActive).Select(r => (Guid?)r.Id).SingleOrDefaultAsync(token);
                var ceiling = ownerRole is { } owner ? (await RoleDefaultsAsync(owner, token)).ToHashSet(StringComparer.Ordinal) : [];
                granted.IntersectWith(ceiling);
            }
        }
        var activeCodes = active.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);
        return new(role, store, user.Version, memberVersion, granted.Where(c => activeCodes.Contains(c)
            && PermissionCatalog.ByCode.TryGetValue(c, out var definition) && definition.AllowsRole(role)).Order(StringComparer.Ordinal).ToList());
    }

    internal Task<List<string>> RoleDefaultsAsync(Guid roleId, CancellationToken token) =>
        (from r in db.RolePermissions.AsNoTracking()
         join p in db.Permissions.AsNoTracking() on r.PermissionId equals p.Id
         where r.RoleId == roleId && r.IsGranted && p.IsActive select p.Code).ToListAsync(token);
}
