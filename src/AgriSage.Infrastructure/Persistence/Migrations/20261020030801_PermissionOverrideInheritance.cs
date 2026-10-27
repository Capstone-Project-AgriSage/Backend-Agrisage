using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PermissionOverrideInheritance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "is_granted",
                table: "store_member_permissions",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A rollback must not silently turn an inherited permission into a Deny.
            // Refuse it until inherited rows have been explicitly resolved by an operator.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM store_member_permissions WHERE is_granted IS NULL) THEN
                        RAISE EXCEPTION 'Cannot roll back PermissionOverrideInheritance while inherited overrides exist.';
                    END IF;
                END $$;
                """);
            migrationBuilder.AlterColumn<bool>(
                name: "is_granted",
                table: "store_member_permissions",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);
        }
    }
}
