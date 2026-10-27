using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Permissions;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

public sealed class PermissionInheritancePostgresTests
{
    [RealDbFact]
    public async Task Reset_preserves_rows_follows_future_role_changes_and_keeps_authority_audit_and_versions()
    {
        var token = TestContext.Current.CancellationToken;
        await using var session = await RealDb.Session.StartAsync();
        await using var db = session.NewContext();
        // All tables/data live in a fresh schema inside a rollback-only transaction, never public.
        var schema = "permission_inheritance_" + Guid.NewGuid().ToString("N");
        var setup = "CREATE SCHEMA " + schema + "; SET LOCAL search_path TO " + schema + ";";
        await db.Database.ExecuteSqlRawAsync(setup, token);
        var script = db.GetService<IMigrator>().GenerateScript(null, null, MigrationsSqlGenerationOptions.NoTransactions);
        await db.Database.ExecuteSqlRawAsync(script, token);

        var adminRole = new Role(RoleCode.Admin, "Admin");
        var ownerRole = new Role(RoleCode.StoreOwner, "Owner");
        var saleRole = new Role(RoleCode.SalesStaff, "Sale");
        var admin = new User(adminRole.Id, "Admin", "hash", "admin@example.test", null);
        var owner = new User(ownerRole.Id, "Owner", "hash", "owner@example.test", null);
        var sale = new User(saleRole.Id, "Sale", "hash", "sale@example.test", null);
        var store = new Store("INHERIT-TEST", "Test store", "Address", "Province");
        var member = new StoreMember(store.Id, sale.Id);
        db.AddRange(adminRole, ownerRole, saleRole, admin, owner, sale, store, member, new StoreMember(store.Id, owner.Id));
        foreach (var definition in PermissionCatalog.Entries)
            foreach (var (role, code) in new[] { (ownerRole, "STORE_OWNER"), (saleRole, "SALES_STAFF") })
                if (definition.DefaultRoles.Contains(code)) db.RolePermissions.Add(new(role.Id, definition.Id, true));
        await db.SaveChangesAsync(token);

        PermissionConfigurationService Service(User user, string role)
        {
            session.CurrentUser.UserId = user.Id; session.CurrentUser.Role = role;
            return new(db, session.CurrentUser, new PermissionEvaluator(db, session.CurrentUser), new PermissionWriteLock(db),
                new AuditTrail(db, session.CurrentUser, new DateTimeProvider()));
        }
        async Task<bool> Has(string code)
        {
            session.CurrentUser.UserId = sale.Id; session.CurrentUser.Role = "SALES_STAFF";
            return await new PermissionEvaluator(db, session.CurrentUser).HasAsync(code, token);
        }

        var configuration = Service(owner, "STORE_OWNER");
        var initial = await configuration.MemberAsync(sale.Id, token);
        var granted = await configuration.SetMemberAsync(sale.Id, new([new("PRODUCTS.CREATE", true), new("ORDERS.READ", false)],
            initial.Version, initial.RoleVersion, "Assign duties"), token);
        Assert.True(await Has("PRODUCTS.CREATE"));
        Assert.False(await Has("ORDERS.READ"));
        var ids = await db.StoreMemberPermissions.Where(p => p.StoreMemberId == member.Id).Select(p => p.Id).ToListAsync(token);
        configuration = Service(owner, "STORE_OWNER");
        var reset = await configuration.SetMemberAsync(sale.Id, new([new("PRODUCTS.CREATE", null), new("ORDERS.READ", null)],
            granted.Version, granted.RoleVersion, "Follow Role"), token);
        Assert.Empty(reset.Overrides);
        Assert.Equal(granted.Version + 1, reset.Version);
        Assert.False(await Has("PRODUCTS.CREATE"));
        Assert.True(await Has("ORDERS.READ"));
        var rows = await db.StoreMemberPermissions.Where(p => p.StoreMemberId == member.Id).ToListAsync(token);
        Assert.Equal(ids.Order(), rows.Select(p => p.Id).Order());
        Assert.All(rows, row => Assert.Null(row.IsGranted));
        Assert.Contains("PRODUCTS.CREATE", reset.PermissionCeiling);

        configuration = Service(owner, "STORE_OWNER");
        await Assert.ThrowsAsync<ConflictException>(() => configuration.SetMemberAsync(sale.Id,
            new([new("ORDERS.READ", null)], granted.Version, granted.RoleVersion, "Stale reset"), token));
        await Assert.ThrowsAsync<ForbiddenException>(() => configuration.SetMemberAsync(sale.Id,
            new([new("AUDIT.READ", null)], reset.Version, reset.RoleVersion, "Unauthorized reset"), token));
        var unchanged = await configuration.SetMemberAsync(sale.Id, new([new("ORDERS.READ", null)], reset.Version, reset.RoleVersion, "No-op reset"), token);
        Assert.Equal(reset.Version, unchanged.Version);
        Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.EntityId == sale.Id && a.Action == "STAFF_PERMISSIONS_CHANGED", token));

        configuration = Service(admin, "ADMIN");
        var saleConfiguration = (await configuration.RolesAsync(token)).Single(r => r.Id == saleRole.Id);
        await configuration.SetRoleAsync(saleRole.Id, new(saleConfiguration.PermissionCodes.Where(c => c != "ORDERS.READ").Append("PRODUCTS.CREATE").ToList(), saleConfiguration.Version, "Change Role defaults"), token);
        Assert.False(await Has("ORDERS.READ"));
        Assert.True(await Has("PRODUCTS.CREATE"));
    }
}
