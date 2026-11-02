using AgriSage.Application.Features.Permissions;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Migrations;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// F3.10: permissions of the crop-season debt flow (F3.7 §4.6, D6 dropped so no rollover-request codes).
public class CropSeasonDebtPermissionsMigrationTests
{
    public static readonly string[] NewCodes =
        ["CROP_SEASONS.READ", "CROP_SEASONS.MANAGE", "DEBT_POLICY.READ", "DEBT_POLICY.MANAGE", "DEBT.ROLLOVER", "MY_CROP_SEASONS.READ"];
    private const string Previous = "20261102203458_CropSeasonsAndDebtPolicy";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("CROP_SEASONS.READ", true, new[] { "ADMIN", "SALES_STAFF", "STORE_OWNER" })]
    [InlineData("CROP_SEASONS.MANAGE", false, new[] { "ADMIN", "STORE_OWNER" })]
    [InlineData("DEBT_POLICY.READ", true, new[] { "ADMIN", "SALES_STAFF", "STORE_OWNER" })]
    [InlineData("DEBT_POLICY.MANAGE", false, new[] { "ADMIN", "STORE_OWNER" })]
    [InlineData("DEBT.ROLLOVER", true, new[] { "ADMIN", "SALES_STAFF", "STORE_OWNER" })]
    [InlineData("MY_CROP_SEASONS.READ", false, new[] { "FARMER" })]
    public void Catalog_lists_the_new_codes_with_their_reviewed_defaults(string code, bool delegable, string[] roles)
    {
        var permission = PermissionCatalog.ByCode[code];
        Assert.Equal(delegable, permission.Delegable);
        Assert.Equal(roles, permission.DefaultRoles.Order(StringComparer.Ordinal));
    }

    // Season dates and the debt policy decide due dates for everyone: Sale staff can never manage them.
    [Fact]
    public void Managing_seasons_and_the_policy_stays_with_the_owner()
    {
        Assert.False(PermissionCatalog.ByCode["CROP_SEASONS.MANAGE"].AllowsRole("SALES_STAFF"));
        Assert.False(PermissionCatalog.ByCode["DEBT_POLICY.MANAGE"].AllowsRole("SALES_STAFF"));
        Assert.False(PermissionCatalog.ByCode["MY_CROP_SEASONS.READ"].AllowsRole("SALES_STAFF"));
        Assert.All(NewCodes.Where(c => !c.StartsWith("MY_", StringComparison.Ordinal)),
            code => Assert.DoesNotContain("FARMER", PermissionCatalog.ByCode[code].DefaultRoles));
    }

    [Fact]
    public void Up_inserts_the_permissions_and_their_role_defaults_and_down_removes_grants_first()
    {
        var ops = new CropSeasonDebtPermissions().UpOperations;
        Assert.All(ops, o => Assert.True(o is InsertDataOperation or SqlOperation, o.GetType().Name));
        var insert = Assert.Single(ops.OfType<InsertDataOperation>());
        Assert.Equal("permissions", insert.Table);
        var codeColumn = Array.IndexOf(insert.Columns, "code");
        Assert.Equal(NewCodes.Order(), Enumerable.Range(0, insert.Values.GetLength(0)).Select(i => (string)insert.Values[i, codeColumn]!).Order());

        var sql = Assert.Single(ops.OfType<SqlOperation>()).Sql;
        Assert.Contains("ON CONFLICT (role_id, permission_id) DO NOTHING", sql);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", sql, StringComparison.OrdinalIgnoreCase);
        foreach (var code in NewCodes)
        {
            var permission = PermissionCatalog.ByCode[code];
            Assert.Contains($"('ADMIN', '{permission.Id}'::uuid)", sql);
            foreach (var role in permission.DefaultRoles) Assert.Contains($"('{role}', '{permission.Id}'::uuid)", sql);
        }

        var down = new CropSeasonDebtPermissions().DownOperations;
        var cleanup = Assert.IsType<SqlOperation>(down[0]).Sql;
        Assert.Contains("DELETE FROM store_member_permissions", cleanup);
        Assert.Contains("DELETE FROM role_permissions", cleanup);
        Assert.All(NewCodes, code => Assert.Contains(PermissionCatalog.ByCode[code].Id.ToString(), cleanup));
        Assert.All(down.Skip(1), o => Assert.IsType<DeleteDataOperation>(o));
    }

    // Real PostgreSQL: existing roles receive exactly their defaults; a grant someone already made is not duplicated.
    [CreditDbFact]
    public async Task Migration_grants_role_defaults_once_on_real_postgres()
    {
        var admin = Environment.GetEnvironmentVariable("AGRISAGE_CREDIT_TEST_CONNECTION_STRING")!;
        var schema = "f310_migration_" + Guid.NewGuid().ToString("N");
        await Exec(admin, $"CREATE SCHEMA \"{schema}\"");
        var connection = new NpgsqlConnectionStringBuilder(admin) { SearchPath = schema, Pooling = false }.ConnectionString;
        AgriSageDbContext Context() => new(new DbContextOptionsBuilder<AgriSageDbContext>().UseNpgsql(connection).Options);
        try
        {
            await using (var db = Context()) await db.GetService<IMigrator>().MigrateAsync(Previous, Token);
            await Exec(connection, """
                INSERT INTO roles (id, code, name, created_at, updated_at) VALUES
                  (gen_random_uuid(), 'ADMIN', 'Admin', now(), now()), (gen_random_uuid(), 'STORE_OWNER', 'Owner', now(), now()),
                  (gen_random_uuid(), 'SALES_STAFF', 'Sale', now(), now()), (gen_random_uuid(), 'DELIVERY_STAFF', 'Delivery', now(), now()),
                  (gen_random_uuid(), 'FARMER', 'Farmer', now(), now());
                """);
            await using (var db = Context()) await db.GetService<IMigrator>().MigrateAsync(null, Token);

            await using var check = Context();
            var granted = await (from rp in check.RolePermissions.AsNoTracking()
                                 join r in check.Roles.AsNoTracking() on rp.RoleId equals r.Id
                                 join p in check.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
                                 where NewCodes.Contains(p.Code) && rp.IsGranted
                                 select new { RoleCode = r.Code, p.Code }).ToListAsync(Token)
                .ContinueWith(t => t.Result.Select(g => new { Role = g.RoleCode.ToString(), g.Code }).ToList(), Token);
            foreach (var code in NewCodes)
            {
                var expected = PermissionCatalog.ByCode[code].DefaultRoles.Append("ADMIN").Distinct()
                    .Select(r => r.Replace("_", "", StringComparison.Ordinal)).Order(StringComparer.OrdinalIgnoreCase);
                Assert.Equal(expected, granted.Where(g => g.Code == code).Select(g => g.Role).Order(StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);
            }
            Assert.DoesNotContain(granted, g => g.Role == "DeliveryStaff");
            Assert.All(await check.Permissions.AsNoTracking().Where(p => NewCodes.Contains(p.Code)).ToListAsync(Token),
                p => Assert.Equal(PermissionCatalog.ByCode[p.Code].Delegable, p.IsDelegable));
        }
        finally
        {
            await Exec(admin, $"DROP SCHEMA \"{schema}\" CASCADE");
        }
    }

    private static async Task Exec(string connectionString, string sql)
    {
        await using var c = new NpgsqlConnection(connectionString);
        await c.OpenAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand(sql, c);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
