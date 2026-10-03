using AgriSage.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// Reviews CustomerGroupDefaultCreditTier without a database (debt term by customer type, database design §35.20):
// one nullable column with its index and a non-cascading foreign key, nothing else.
public class CustomerGroupCreditTierMigrationTests
{
    [Fact]
    public void Up_adds_one_nullable_column_its_index_and_a_non_cascading_foreign_key()
    {
        var up = new CustomerGroupDefaultCreditTier().UpOperations;

        Assert.Collection(
            up,
            operation =>
            {
                var add = Assert.IsType<AddColumnOperation>(operation);
                Assert.Equal(("customer_groups", "default_credit_tier_id", "uuid", true), (add.Table, add.Name, add.ColumnType, add.IsNullable));
                Assert.Null(add.DefaultValue);
                Assert.Null(add.DefaultValueSql);
            },
            operation =>
            {
                var index = Assert.IsType<CreateIndexOperation>(operation);
                Assert.Equal(("customer_groups", "ix_customer_groups_default_credit_tier_id"), (index.Table, index.Name));
                Assert.Equal(["default_credit_tier_id"], index.Columns);
                Assert.False(index.IsUnique);
            },
            operation =>
            {
                var foreignKey = Assert.IsType<AddForeignKeyOperation>(operation);
                Assert.Equal("fk_customer_groups_default_credit_tier_id", foreignKey.Name);
                Assert.Equal(("customer_groups", "credit_tiers"), (foreignKey.Table, foreignKey.PrincipalTable));
                Assert.Equal(["default_credit_tier_id"], foreignKey.Columns);
                Assert.Equal(["id"], foreignKey.PrincipalColumns!);
                Assert.Equal(ReferentialAction.NoAction, foreignKey.OnDelete);
            });
    }

    [Fact]
    public void Down_only_removes_what_up_added()
    {
        var down = new CustomerGroupDefaultCreditTier().DownOperations;

        Assert.Collection(
            down,
            operation => Assert.Equal("fk_customer_groups_default_credit_tier_id", Assert.IsType<DropForeignKeyOperation>(operation).Name),
            operation => Assert.Equal("ix_customer_groups_default_credit_tier_id", Assert.IsType<DropIndexOperation>(operation).Name),
            operation =>
            {
                var drop = Assert.IsType<DropColumnOperation>(operation);
                Assert.Equal(("customer_groups", "default_credit_tier_id"), (drop.Table, drop.Name));
            });
    }
}
