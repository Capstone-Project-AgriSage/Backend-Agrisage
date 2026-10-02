namespace AgriSage.Api.Extensions;

// Role gates for [Authorize(Roles = ...)]; role codes are the JWT `role` claim values.
public static class ApiRoles
{
    // Create/change data: Admin and Store Owner.
    public const string Manage = "ADMIN,STORE_OWNER";

    // Read internal data: every staff role.
    public const string Read = "ADMIN,STORE_OWNER,SALES_STAFF,DELIVERY_STAFF";
}
