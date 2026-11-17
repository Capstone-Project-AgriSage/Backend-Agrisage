using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DebtReminderStartDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "reminders_start_date",
                table: "store_debt_policies",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reminders_start_date",
                table: "store_debt_policies");
        }
    }
}
