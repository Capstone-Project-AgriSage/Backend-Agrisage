using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LockDebtPermissionDelegation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("46a90a7a-3994-f3b2-6611-4c439280e587"),
                column: "is_delegable",
                value: false);

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("627dae2e-7814-b747-4a18-a4a42938de57"),
                column: "is_delegable",
                value: false);

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("8250ff60-b1f6-a423-8d4b-e24f18692936"),
                column: "is_delegable",
                value: false);

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("9f33832a-1a89-e65d-a419-85d20ff9e062"),
                column: "is_delegable",
                value: false);

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("ab3d58f3-b46e-4bea-da41-d14f270f38d0"),
                column: "is_delegable",
                value: false);

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("c77147d9-b74d-612f-1cf1-527c23673ddd"),
                column: "is_delegable",
                value: false);

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("cd122a99-61bd-683f-491a-19e1ca097e7a"),
                column: "is_delegable",
                value: false);

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("dd6533ae-c6e0-5307-f655-1587987ab231"),
                column: "is_delegable",
                value: false);

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("f02cca7a-535e-1c66-cf85-d55480285eef"),
                column: "is_delegable",
                value: false);

            // F3.21 part 2 (option B, data only): the nine owner-only actions cannot be delegated. Grants already given to a
            // non-owner role or member are switched off, never deleted; inherited (NULL) overrides stay and now inherit "no";
            // ADMIN and STORE_OWNER rows are untouched. Each changed member and role gets its version bumped (open permission
            // screens reload) and one audit row with no actor. No temporary table: the SQL must also run without a transaction.
            migrationBuilder.Sql(OwnerOnlyCodes + """
                changed AS (
                  UPDATE store_member_permissions smp SET is_granted = FALSE, updated_at = now()
                  FROM store_members sm, users u, roles r, permissions p
                  WHERE p.id = smp.permission_id AND p.code IN (SELECT code FROM f321_codes) AND smp.is_granted IS TRUE
                    AND sm.id = smp.store_member_id AND u.id = sm.user_id AND r.id = u.role_id
                    AND r.code NOT IN ('ADMIN', 'STORE_OWNER')
                  RETURNING smp.store_member_id, p.code),
                per_member AS (
                  SELECT store_member_id, jsonb_agg(code ORDER BY code) AS codes FROM changed GROUP BY store_member_id),
                bumped AS (
                  UPDATE store_members sm SET version = sm.version + 1, updated_at = now()
                  FROM per_member pm WHERE sm.id = pm.store_member_id
                  RETURNING sm.id, sm.user_id, sm.store_id)
                INSERT INTO audit_logs (id, store_id, actor_user_id, action, entity_type, entity_id, old_values, new_values, reason, occurred_at)
                SELECT gen_random_uuid(), b.store_id, NULL, 'STAFF_PERMISSIONS_CHANGED', 'STAFF', b.user_id,
                  jsonb_build_object('revokedCodes', pm.codes, 'granted', true),
                  jsonb_build_object('revokedCodes', pm.codes, 'granted', false),
                  'F3.21: owner-only debt and credit actions cannot be delegated (migration LockDebtPermissionDelegation).', now()
                FROM bumped b JOIN per_member pm ON pm.store_member_id = b.id;
                """);
            migrationBuilder.Sql(OwnerOnlyCodes + """
                changed AS (
                  UPDATE role_permissions rp SET is_granted = FALSE, updated_at = now()
                  FROM roles r, permissions p
                  WHERE p.id = rp.permission_id AND p.code IN (SELECT code FROM f321_codes) AND rp.is_granted
                    AND r.id = rp.role_id AND r.code NOT IN ('ADMIN', 'STORE_OWNER')
                  RETURNING rp.role_id, p.code),
                per_role AS (
                  SELECT role_id, jsonb_agg(code ORDER BY code) AS codes FROM changed GROUP BY role_id),
                bumped AS (
                  UPDATE roles r SET version = r.version + 1, updated_at = now()
                  FROM per_role pr WHERE r.id = pr.role_id RETURNING r.id)
                INSERT INTO audit_logs (id, store_id, actor_user_id, action, entity_type, entity_id, old_values, new_values, reason, occurred_at)
                SELECT gen_random_uuid(), NULL, NULL, 'ROLE_PERMISSIONS_CHANGED', 'ROLE', b.id,
                  jsonb_build_object('revokedCodes', pr.codes, 'granted', true),
                  jsonb_build_object('revokedCodes', pr.codes, 'granted', false),
                  'F3.21: owner-only debt and credit actions cannot be delegated (migration LockDebtPermissionDelegation).', now()
                FROM bumped b JOIN per_role pr ON pr.role_id = b.id;
                """);
        }

        // The opening of a WITH clause naming the nine owner-only permission codes; the statement appended to it continues
        // the same WITH clause with its own CTEs.
        private const string OwnerOnlyCodes = """
            WITH f321_codes(code) AS (VALUES
              ('DEBT.ADJUST'), ('DEBT.CANCEL'), ('DEBT.MANUAL'), ('DEBT.DUE_DATE'),
              ('BANK_PAYMENTS.CONFIRM'), ('BANK_PAYMENTS.REJECT'),
              ('CREDIT.SUSPEND'), ('CREDIT.BLOCK'), ('CREDIT.ACTIVATE')),

            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only the catalog flag is restored. Revoked grants are NOT re-granted: doing so would reopen the hole, and a
            // rollback cannot tell the rows this migration changed from rows a person changed afterwards.
            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("46a90a7a-3994-f3b2-6611-4c439280e587"),
                column: "is_delegable",
                value: true);

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("627dae2e-7814-b747-4a18-a4a42938de57"),
                column: "is_delegable",
                value: true);

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("8250ff60-b1f6-a423-8d4b-e24f18692936"),
                column: "is_delegable",
                value: true);

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("9f33832a-1a89-e65d-a419-85d20ff9e062"),
                column: "is_delegable",
                value: true);

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("ab3d58f3-b46e-4bea-da41-d14f270f38d0"),
                column: "is_delegable",
                value: true);

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("c77147d9-b74d-612f-1cf1-527c23673ddd"),
                column: "is_delegable",
                value: true);

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("cd122a99-61bd-683f-491a-19e1ca097e7a"),
                column: "is_delegable",
                value: true);

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("dd6533ae-c6e0-5307-f655-1587987ab231"),
                column: "is_delegable",
                value: true);

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("f02cca7a-535e-1c66-cf85-d55480285eef"),
                column: "is_delegable",
                value: true);
        }
    }
}
