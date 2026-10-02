using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentOrderLinkAndOrderRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_refunds_sales_return_id",
                table: "refunds");

            migrationBuilder.AlterColumn<Guid>(
                name: "sales_return_id",
                table: "refunds",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "order_id",
                table: "refunds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "order_id",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_refunds_order_id",
                table: "refunds",
                column: "order_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refunds_order_payment",
                table: "refunds",
                sql: "order_id IS NULL OR original_payment_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refunds_source",
                table: "refunds",
                sql: "num_nonnulls(sales_return_id, order_id) = 1");

            migrationBuilder.CreateIndex(
                name: "ix_payments_order_id",
                table: "payments",
                column: "order_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_order_context",
                table: "payments",
                sql: "(payment_context = 'ORDER_PAYMENT' AND order_id IS NOT NULL) OR (payment_context = 'DEBT_REPAYMENT' AND order_id IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_payments_order_id",
                table: "payments",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_refunds_order_id",
                table: "refunds",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_refunds_sales_return_id",
                table: "refunds",
                column: "sales_return_id",
                principalTable: "sales_returns",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payments_order_id",
                table: "payments");

            migrationBuilder.DropForeignKey(
                name: "fk_refunds_order_id",
                table: "refunds");

            migrationBuilder.DropForeignKey(
                name: "fk_refunds_sales_return_id",
                table: "refunds");

            migrationBuilder.DropIndex(
                name: "ix_refunds_order_id",
                table: "refunds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refunds_order_payment",
                table: "refunds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refunds_source",
                table: "refunds");

            migrationBuilder.DropIndex(
                name: "ix_payments_order_id",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_order_context",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "order_id",
                table: "refunds");

            migrationBuilder.DropColumn(
                name: "order_id",
                table: "payments");

            // Intentionally fails while cancelled-order refunds exist (their sales_return_id is NULL): rolling back
            // must not silently drop refund history. No default value is added (the original column had none).
            migrationBuilder.AlterColumn<Guid>(
                name: "sales_return_id",
                table: "refunds",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_refunds_sales_return_id",
                table: "refunds",
                column: "sales_return_id",
                principalTable: "sales_returns",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
