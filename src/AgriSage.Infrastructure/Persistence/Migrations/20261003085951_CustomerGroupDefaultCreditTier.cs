using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerGroupDefaultCreditTier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "default_credit_tier_id",
                table: "customer_groups",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_groups_default_credit_tier_id",
                table: "customer_groups",
                column: "default_credit_tier_id");

            migrationBuilder.AddForeignKey(
                name: "fk_customer_groups_default_credit_tier_id",
                table: "customer_groups",
                column: "default_credit_tier_id",
                principalTable: "credit_tiers",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_customer_groups_default_credit_tier_id",
                table: "customer_groups");

            migrationBuilder.DropIndex(
                name: "ix_customer_groups_default_credit_tier_id",
                table: "customer_groups");

            migrationBuilder.DropColumn(
                name: "default_credit_tier_id",
                table: "customer_groups");
        }
    }
}
