using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EventDrivenRealtime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "realtime_outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    topic = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<long>(type: "bigint", nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_realtime_outbox", x => x.id);
                    table.CheckConstraint("ck_realtime_outbox_attempts", "attempts >= 0");
                    table.CheckConstraint("ck_realtime_outbox_scope", "scope IN ('user', 'role', 'member', 'store', 'all', 'case', 'movement')");
                    table.CheckConstraint("ck_realtime_outbox_topic", "topic IN ('notifications', 'permissions', 'inventory', 'diagnosis', 'stock-posted')");
                    table.ForeignKey(
                        name: "fk_realtime_outbox_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_realtime_outbox_available_at_id",
                table: "realtime_outbox",
                columns: new[] { "available_at", "id" },
                filter: "processed_at IS NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_realtime_outbox_transaction_id_topic_scope_subject_id",
                table: "realtime_outbox",
                columns: new[] { "transaction_id", "topic", "scope", "subject_id" },
                unique: true);

            migrationBuilder.Sql(RealtimeOutboxSql.Install);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RealtimeOutboxSql.Uninstall);
            migrationBuilder.DropTable(
                name: "realtime_outbox");
        }
    }
}
