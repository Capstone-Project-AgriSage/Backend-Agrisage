using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryLocationTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "delivery_location_points",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    delivery_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: false),
                    longitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: false),
                    accuracy_meters = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_location_points", x => x.id);
                    table.CheckConstraint("ck_delivery_location_points_accuracy_meters", "accuracy_meters IS NULL OR accuracy_meters >= 0");
                    table.CheckConstraint("ck_delivery_location_points_latitude", "latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_delivery_location_points_longitude", "longitude BETWEEN -180 AND 180");
                    table.ForeignKey(
                        name: "fk_delivery_location_points_delivery_attempt_id",
                        column: x => x.delivery_attempt_id,
                        principalTable: "delivery_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_delivery_location_points_delivery_attempt_id_recorded_at",
                table: "delivery_location_points",
                columns: new[] { "delivery_attempt_id", "recorded_at" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_location_points");
        }
    }
}
