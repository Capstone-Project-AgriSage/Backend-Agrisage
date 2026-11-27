using AgriSage.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// DATABASE_DESIGN §35.26 (F2.8): one new append-only table for the driver's positions. No permission, no existing table
// or column changes (the delivery permissions are reused).
public class DeliveryLocationTrackingMigrationTests
{
    [Fact]
    public void Up_only_creates_the_location_table_and_its_unique_index()
    {
        var migration = new DeliveryLocationTracking();
        Assert.All(migration.UpOperations, o => Assert.True(
            o is CreateTableOperation or CreateIndexOperation, $"Unexpected operation {o.GetType().Name}"));

        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("delivery_location_points", table.Name);
        Assert.Equal(
            ["id", "delivery_attempt_id", "latitude", "longitude", "accuracy_meters", "recorded_at", "created_at", "updated_at"],
            table.Columns.Select(c => c.Name));
        Assert.Equal("numeric(10,7)", table.Columns.Single(c => c.Name == "latitude").ColumnType);
        Assert.Equal("numeric(10,7)", table.Columns.Single(c => c.Name == "longitude").ColumnType);
        Assert.Equal("numeric(8,2)", table.Columns.Single(c => c.Name == "accuracy_meters").ColumnType);
        Assert.True(table.Columns.Single(c => c.Name == "accuracy_meters").IsNullable);
        Assert.All(table.Columns.Where(c => c.Name != "accuracy_meters"), c => Assert.False(c.IsNullable, c.Name));
        // Append-only: no soft delete, no version.
        Assert.DoesNotContain(table.Columns, c => c.Name is "deleted_at" or "deleted_by" or "version");

        Assert.Contains(table.CheckConstraints, c => c.Name == "ck_delivery_location_points_latitude"
            && c.Sql == "latitude BETWEEN -90 AND 90");
        Assert.Contains(table.CheckConstraints, c => c.Name == "ck_delivery_location_points_longitude"
            && c.Sql == "longitude BETWEEN -180 AND 180");
        Assert.Contains(table.CheckConstraints, c => c.Name == "ck_delivery_location_points_accuracy_meters"
            && c.Sql == "accuracy_meters IS NULL OR accuracy_meters >= 0");

        // Business references never cascade (database rules).
        var fk = Assert.Single(table.ForeignKeys);
        Assert.Equal("delivery_attempts", fk.PrincipalTable);
        Assert.Equal(ReferentialAction.Restrict, fk.OnDelete);

        var index = Assert.Single(migration.UpOperations.OfType<CreateIndexOperation>());
        Assert.True(index.IsUnique);
        Assert.Equal(["delivery_attempt_id", "recorded_at"], index.Columns);
        Assert.Null(index.Filter);
    }

    [Fact]
    public void Down_only_drops_the_table()
    {
        var operation = Assert.Single(new DeliveryLocationTracking().DownOperations);
        Assert.Equal("delivery_location_points", Assert.IsType<DropTableOperation>(operation).Name);
    }
}
