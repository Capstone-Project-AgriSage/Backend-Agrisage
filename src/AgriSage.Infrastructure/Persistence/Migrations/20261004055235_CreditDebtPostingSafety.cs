using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreditDebtPostingSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_payment_method",
                table: "payments");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_payment_method",
                table: "payments",
                sql: "payment_method IN ('CASH', 'PAYOS', 'BANK_TRANSFER')");

            migrationBuilder.CreateIndex(
                name: "ux_debt_transactions_payment_source",
                table: "debt_transactions",
                column: "payment_allocation_id",
                unique: true,
                filter: "payment_allocation_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_debt_entries_fulfillment_source",
                table: "debt_entries",
                column: "source_stock_movement_id",
                unique: true,
                filter: "source_stock_movement_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_payment_method",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ux_debt_transactions_payment_source",
                table: "debt_transactions");

            migrationBuilder.DropIndex(
                name: "ux_debt_entries_fulfillment_source",
                table: "debt_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_payment_method",
                table: "payments",
                sql: "payment_method IN ('CASH', 'PAYOS')");
        }
    }
}
