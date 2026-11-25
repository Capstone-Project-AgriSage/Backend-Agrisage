using AgriSage.Application.Features.Permissions;
using AgriSage.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// DATABASE_DESIGN §35.24 (F3.23): one new append-only table for reminders made outside the system, and one delegable
// permission for owner and Sale staff. Nothing existing changes.
public class DebtFollowUpsMigrationTests
{
    public static readonly string[] NewCodes = ["DEBT.FOLLOW_UP"];

    [Fact]
    public void Up_only_creates_the_follow_up_table_and_its_permission()
    {
        var migration = new DebtFollowUps();
        Assert.All(migration.UpOperations, o => Assert.True(
            o is CreateTableOperation or InsertDataOperation or CreateIndexOperation or SqlOperation,
            $"Unexpected operation {o.GetType().Name}"));

        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("debt_follow_ups", table.Name);
        Assert.Equal(
            ["id", "debt_account_id", "channel", "outcome", "note", "contacted_at", "promised_date", "promised_amount",
             "outstanding_amount", "overdue_amount", "created_by", "created_at", "updated_at", "deleted_at", "deleted_by"],
            table.Columns.Select(c => c.Name));
        Assert.Equal("numeric(18,2)", table.Columns.Single(c => c.Name == "promised_amount").ColumnType);
        Assert.True(table.Columns.Single(c => c.Name == "note").IsNullable);
        Assert.Equal(1000, table.Columns.Single(c => c.Name == "note").MaxLength);

        Assert.Contains(table.CheckConstraints, c => c.Name == "ck_debt_follow_ups_channel"
            && c.Sql == "channel IN ('PHONE_CALL', 'VISIT', 'AT_STORE', 'MESSAGE', 'OTHER')");
        Assert.Contains(table.CheckConstraints, c => c.Name == "ck_debt_follow_ups_outcome"
            && c.Sql == "outcome IN ('PROMISED', 'ASKED_MORE_TIME', 'NO_ANSWER', 'REFUSED', 'OTHER')");
        Assert.Contains(table.CheckConstraints, c => c.Name == "ck_debt_follow_ups_promise");
        Assert.Contains(table.CheckConstraints, c => c.Name == "ck_debt_follow_ups_promised_amount");
        Assert.Contains(table.CheckConstraints, c => c.Name == "ck_debt_follow_ups_amounts");

        // Business references never cascade (database rules).
        Assert.All(table.ForeignKeys, fk => Assert.True(
            fk.OnDelete is ReferentialAction.Restrict or ReferentialAction.NoAction, $"{fk.Name} deletes {fk.OnDelete}"));
        Assert.Contains(table.ForeignKeys, fk => fk.PrincipalTable == "debt_accounts");

        var index = Assert.Single(migration.UpOperations.OfType<CreateIndexOperation>());
        Assert.Equal(["debt_account_id", "contacted_at"], index.Columns);
        Assert.Equal([false, true], index.IsDescending!);

        var insert = Assert.Single(migration.UpOperations.OfType<InsertDataOperation>());
        Assert.Equal("permissions", insert.Table);
        Assert.Equal(1, insert.Values.GetLength(0));
        var definition = Assert.Single(PermissionCatalog.Entries, p => p.Code == "DEBT.FOLLOW_UP");
        Assert.True(definition.Delegable);
        var sql = Assert.Single(migration.UpOperations.OfType<SqlOperation>()).Sql;
        foreach (var role in new[] { "ADMIN", "STORE_OWNER", "SALES_STAFF" })
        {
            Assert.Contains(role, definition.DefaultRoles);
            Assert.Contains($"('{role}', '{definition.Id}'::uuid)", sql);
        }

        Assert.DoesNotContain("FARMER", sql);
        Assert.DoesNotContain("DELIVERY_STAFF", sql);
        Assert.Contains("ON CONFLICT (role_id, permission_id) DO NOTHING", sql);
    }

    [Fact]
    public void Down_drops_the_table_and_removes_permission_references_before_the_permission()
    {
        var operations = new DebtFollowUps().DownOperations;
        Assert.Equal("debt_follow_ups", Assert.IsType<DropTableOperation>(operations[0]).Name);
        var cleanup = Assert.IsType<SqlOperation>(operations[1]).Sql;
        Assert.Contains("DELETE FROM store_member_permissions", cleanup);
        Assert.Contains("DELETE FROM role_permissions", cleanup);
        Assert.IsType<DeleteDataOperation>(operations[2]);
        Assert.Equal(3, operations.Count);
    }
}
