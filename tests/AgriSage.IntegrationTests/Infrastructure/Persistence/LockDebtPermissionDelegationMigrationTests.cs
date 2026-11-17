using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Permissions;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Interceptors;
using AgriSage.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// F3.21 part 2 (option B): the nine owner-only debt and credit actions cannot be delegated. The catalog says so, the data
// migration LockDebtPermissionDelegation revokes existing grants to non-owner roles and members, and the permission screens
// no longer offer the codes.
public class LockDebtPermissionDelegationMigrationTests
{
    public static readonly string[] OwnerOnly =
    [
        "DEBT.ADJUST", "DEBT.CANCEL", "DEBT.MANUAL", "DEBT.DUE_DATE", "BANK_PAYMENTS.CONFIRM", "BANK_PAYMENTS.REJECT",
        "CREDIT.SUSPEND", "CREDIT.BLOCK", "CREDIT.ACTIVATE",
    ];
    private const string Previous = "20261102090829_InventoryReplenishment";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Catalog_makes_the_nine_owner_only_codes_non_delegable_and_owner_defaults_only()
    {
        foreach (var code in OwnerOnly)
        {
            var definition = PermissionCatalog.ByCode[code];
            Assert.False(definition.Delegable, code);
            Assert.Equal(new[] { "ADMIN", "STORE_OWNER" }, definition.DefaultRoles.Order(StringComparer.Ordinal));
            Assert.False(definition.AllowsRole("SALES_STAFF"), code);
            Assert.True(definition.AllowsRole("STORE_OWNER"), code);
        }
    }

    [Fact]
    public void Migration_only_updates_permission_data_and_never_deletes_or_regrants()
    {
        var migration = new LockDebtPermissionDelegation();
        var up = migration.UpOperations;
        var updates = up.OfType<UpdateDataOperation>().ToList();
        Assert.Equal(OwnerOnly.Select(c => PermissionCatalog.ByCode[c].Id).Order(), updates.Select(u => (Guid)u.KeyValues[0, 0]!).Order());
        Assert.All(updates, u => Assert.Equal(("permissions", "is_delegable", (object?)false), (u.Table, u.Columns.Single(), u.Values[0, 0])));
        Assert.All(up, o => Assert.True(o is UpdateDataOperation or SqlOperation, o.GetType().Name));
        var sql = string.Join("\n", up.OfType<SqlOperation>().Select(o => o.Sql));
        Assert.Contains("SET is_granted = FALSE", sql);
        Assert.Contains("NOT IN ('ADMIN', 'STORE_OWNER')", sql);
        Assert.Contains("is_granted IS TRUE", sql);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TEMP", sql, StringComparison.OrdinalIgnoreCase);
        foreach (var code in OwnerOnly) Assert.Contains($"('{code}')", sql);

        var down = migration.DownOperations;
        Assert.All(down, o => Assert.IsType<UpdateDataOperation>(o));
        Assert.All(down.Cast<UpdateDataOperation>(), u => Assert.Equal(("permissions", (object?)true), (u.Table, u.Values[0, 0])));
    }

    // The migration runs on real PostgreSQL data in an isolated schema: the schema is built up to the previous migration,
    // seeded with every shape of grant, then migrated to the latest version.
    [CreditDbFact]
    public async Task Migration_revokes_only_non_owner_grants_of_the_nine_codes_with_audit_and_no_deleted_rows()
    {
        var admin = Environment.GetEnvironmentVariable("AGRISAGE_CREDIT_TEST_CONNECTION_STRING")!;
        var schema = "f321_migration_" + Guid.NewGuid().ToString("N");
        await using (var c = new NpgsqlConnection(admin))
        {
            await c.OpenAsync(Token);
            await using var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", c);
            await create.ExecuteNonQueryAsync(Token);
        }
        var connection = new NpgsqlConnectionStringBuilder(admin) { SearchPath = schema, Pooling = false }.ConnectionString;
        var user = new CreditTestDatabase.UserContext(); var clock = new CreditTestDatabase.TestClock();
        AgriSageDbContext Context() => new(new DbContextOptionsBuilder<AgriSageDbContext>().UseNpgsql(connection)
            .AddInterceptors(new SoftDeleteInterceptor(user, clock), new AuditableEntityInterceptor(clock), new ConcurrencyVersionInterceptor()).Options);
        try
        {
            await using (var db = Context()) await db.GetService<IMigrator>().MigrateAsync(Previous, Token);

            Guid Id(string code) => PermissionCatalog.ByCode[code].Id;
            // The other delegable permission must already exist at the previous migration: later migrations add new
            // delegable codes (for example CONTACT_REQUESTS.READ, 2026-11-12) at the top of the catalog.
            List<Guid> existing;
            await using (var db = Context()) existing = await db.Permissions.AsNoTracking().Select(p => p.Id).ToListAsync(Token);
            var other = PermissionCatalog.Entries.First(p => !OwnerOnly.Contains(p.Code) && p.Delegable && existing.Contains(p.Id)).Id;
            var saleRole = new Role(RoleCode.SalesStaff, "Sale"); var ownerRole = new Role(RoleCode.StoreOwner, "Owner");
            var store = new Store("F321", "Store", "Street", "Province");
            var sale = new User(saleRole.Id, "Sale", "hash", "sale@example.test", null);
            var owner = new User(ownerRole.Id, "Owner", "hash", "owner@example.test", null);
            var saleMember = new StoreMember(store.Id, sale.Id); var ownerMember = new StoreMember(store.Id, owner.Id);
            await using (var db = Context())
            {
                db.AddRange(saleRole, ownerRole, store, sale, owner, saleMember, ownerMember);
                db.RolePermissions.AddRange(new RolePermission(saleRole.Id, Id("DEBT.DUE_DATE"), true), new RolePermission(saleRole.Id, other, true));
                foreach (var code in OwnerOnly) db.RolePermissions.Add(new RolePermission(ownerRole.Id, Id(code), true));
                db.StoreMemberPermissions.AddRange(
                    new StoreMemberPermission(saleMember.Id, Id("DEBT.ADJUST"), true),
                    new StoreMemberPermission(saleMember.Id, Id("BANK_PAYMENTS.CONFIRM"), true),
                    new StoreMemberPermission(saleMember.Id, Id("DEBT.CANCEL"), false),
                    new StoreMemberPermission(saleMember.Id, Id("CREDIT.SUSPEND"), null),
                    new StoreMemberPermission(saleMember.Id, other, true),
                    new StoreMemberPermission(ownerMember.Id, Id("DEBT.ADJUST"), true));
                await db.SaveChangesAsync(Token);
            }

            // Up to this migration only: later migrations add their own role defaults.
            await using (var db = Context()) await db.GetService<IMigrator>().MigrateAsync("20261102193332_LockDebtPermissionDelegation", Token);

            await using var check = Context();
            async Task<bool?> Member(StoreMember m, Guid permission) => await check.StoreMemberPermissions.AsNoTracking()
                .Where(p => p.StoreMemberId == m.Id && p.PermissionId == permission).Select(p => p.IsGranted).SingleAsync(Token);
            async Task<bool> Role(Role r, Guid permission) => await check.RolePermissions.AsNoTracking()
                .Where(p => p.RoleId == r.Id && p.PermissionId == permission).Select(p => p.IsGranted).SingleAsync(Token);

            Assert.False(await Member(saleMember, Id("DEBT.ADJUST")));
            Assert.False(await Member(saleMember, Id("BANK_PAYMENTS.CONFIRM")));
            Assert.False(await Member(saleMember, Id("DEBT.CANCEL")));
            Assert.Null(await Member(saleMember, Id("CREDIT.SUSPEND")));
            Assert.True(await Member(saleMember, other));
            Assert.True(await Member(ownerMember, Id("DEBT.ADJUST")));
            Assert.False(await Role(saleRole, Id("DEBT.DUE_DATE")));
            Assert.True(await Role(saleRole, other));
            foreach (var code in OwnerOnly) Assert.True(await Role(ownerRole, Id(code)), code);
            Assert.Equal(6, await check.StoreMemberPermissions.CountAsync(Token));
            Assert.Equal(2 + OwnerOnly.Length, await check.RolePermissions.CountAsync(Token));

            Assert.Equal(1L, await check.StoreMembers.AsNoTracking().Where(m => m.Id == saleMember.Id).Select(m => m.Version).SingleAsync(Token));
            Assert.Equal(0L, await check.StoreMembers.AsNoTracking().Where(m => m.Id == ownerMember.Id).Select(m => m.Version).SingleAsync(Token));
            Assert.Equal(1L, await check.Roles.AsNoTracking().Where(r => r.Id == saleRole.Id).Select(r => r.Version).SingleAsync(Token));
            Assert.Equal(0L, await check.Roles.AsNoTracking().Where(r => r.Id == ownerRole.Id).Select(r => r.Version).SingleAsync(Token));

            var audits = await check.AuditLogs.AsNoTracking().OrderBy(a => a.Action).ToListAsync(Token);
            Assert.Equal(2, audits.Count);
            Assert.Equal(("ROLE_PERMISSIONS_CHANGED", "ROLE", (Guid?)saleRole.Id, (Guid?)null), (audits[0].Action, audits[0].EntityType, audits[0].EntityId, audits[0].ActorUserId));
            Assert.Equal(("STAFF_PERMISSIONS_CHANGED", "STAFF", (Guid?)sale.Id, (Guid?)store.Id), (audits[1].Action, audits[1].EntityType, audits[1].EntityId, audits[1].StoreId));
            Assert.Contains("DEBT.DUE_DATE", audits[0].NewValues);
            Assert.Contains("BANK_PAYMENTS.CONFIRM", audits[1].NewValues);
            Assert.Contains("DEBT.ADJUST", audits[1].NewValues);
            Assert.DoesNotContain("DEBT.CANCEL", audits[1].NewValues);

            Assert.All(await check.Permissions.AsNoTracking().Where(p => OwnerOnly.Contains(p.Code)).ToListAsync(Token),
                p => Assert.False(p.IsDelegable, p.Code));
        }
        finally
        {
            await using var c = new NpgsqlConnection(admin);
            await c.OpenAsync(CancellationToken.None);
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", c);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    // The member permission screen (GET/PUT /api/staff/{id}/permissions) no longer offers the nine codes, and a request that
    // still tries to grant one is refused before anything is written.
    [CreditDbFact]
    public async Task Member_permission_screen_neither_offers_nor_accepts_the_owner_only_codes()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var staff = await OwnerOnlyDebtDatabaseTests.AddStaffAsync(e, OwnerOnlyDebtDatabaseTests.NoGrant);
        using var s = e.Scope();
        // The fixture registers the Application layer only; supply the Infrastructure write lock the service needs.
        var service = ActivatorUtilities.CreateInstance<PermissionConfigurationService>(s.ServiceProvider,
            new PermissionWriteLock(s.ServiceProvider.GetRequiredService<AgriSageDbContext>()));
        var member = await service.MemberAsync(staff.UserId, Token);
        Assert.NotEmpty(member.GrantablePermissions);
        Assert.DoesNotContain(member.GrantablePermissions, OwnerOnly.Contains);

        foreach (var code in OwnerOnly)
            await Assert.ThrowsAsync<ForbiddenException>(() => service.SetMemberAsync(staff.UserId,
                new([new(code, true)], member.Version, member.RoleVersion, "Try to delegate"), Token));
        Assert.Equal(member.Version, (await service.MemberAsync(staff.UserId, Token)).Version);
    }
}
