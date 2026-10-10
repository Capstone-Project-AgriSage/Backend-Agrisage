using AgriSage.Application.Common;
using AgriSage.Application.Features.Permissions;
using AgriSage.Domain.Features.Identity.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Infrastructure.Persistence.Seed;

internal static class PermissionSeeder
{
    internal static async Task StageAsync(AgriSageDbContext db, CancellationToken token)
    {
        var persisted = await db.Roles.AsNoTracking().ToListAsync(token);
        var roles = persisted.Concat(db.Roles.Local).DistinctBy(r => r.Id).ToList();
        var pairs = (await db.RolePermissions.AsNoTracking().Select(p => new { p.RoleId, p.PermissionId }).ToListAsync(token))
            .Select(p => (p.RoleId, p.PermissionId)).ToHashSet();
        foreach (var role in roles)
        foreach (var permission in PermissionCatalog.Entries.Where(p => EnumText.Format(role.Code) == "ADMIN" || p.DefaultRoles.Contains(EnumText.Format(role.Code))))
            if (pairs.Add((role.Id, permission.Id))) db.RolePermissions.Add(new RolePermission(role.Id, permission.Id, true));
    }
}
