namespace AgriSage.Infrastructure.Persistence;

// Expression indexes that EF Core cannot model (database design §24, §2, §35.14). They are not part of the
// EF model; the InitialCreate migration creates them with migrationBuilder.Sql(...) from these constants.
// FROZEN: InitialCreate references these exact strings. To change an expression index, add new constants
// and a new migration — never edit the existing ones.
public static class PostgreSqlRawIndexes
{
    // Same store product + normalized lot number + expiry date = one logical Inventory Lot (NULL expiry is equal).
    public const string InventoryLotsLogicalLot = """
        CREATE UNIQUE INDEX ux_inventory_lots_logical_lot
        ON inventory_lots (
            store_product_id,
            lower(lot_number),
            expiry_date
        ) NULLS NOT DISTINCT
        WHERE deleted_at IS NULL
          AND lot_number IS NOT NULL;
        """;

    public const string UsersEmailLower = """
        CREATE UNIQUE INDEX ux_users_email_lower
        ON users (LOWER(email))
        WHERE deleted_at IS NULL
          AND email IS NOT NULL;
        """;

    public const string DropInventoryLotsLogicalLot = "DROP INDEX IF EXISTS ux_inventory_lots_logical_lot;";

    public const string DropUsersEmailLower = "DROP INDEX IF EXISTS ux_users_email_lower;";

    public static IReadOnlyList<string> All { get; } = [InventoryLotsLogicalLot, UsersEmailLower];
}
