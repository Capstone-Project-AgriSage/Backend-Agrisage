namespace AgriSage.Api.Extensions;

// Role gates for [Authorize(Roles = ...)]; role codes are the JWT `role` claim values.
public static class ApiRoles
{
    // Create/change data: Admin and Store Owner.
    public const string Manage = "ADMIN,STORE_OWNER";

    // Warehouse back office: draft goods receipts, supplier and stock reads (Admin, Store Owner, Sales). Delivery staff
    // have no access to receiving or stock.
    public const string Operate = "ADMIN,STORE_OWNER,SALES_STAFF";

    // Read internal data: every staff role.
    public const string Read = "ADMIN,STORE_OWNER,SALES_STAFF,DELIVERY_STAFF";
}
