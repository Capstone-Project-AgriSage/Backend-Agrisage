using AgriSage.Domain.Features.Identity.Enums;

namespace AgriSage.Application.Features.Staff;

// Who may manage whom. Admin manages every staff role; a Store Owner manages Sales and Delivery staff only
// (never another Store Owner or an Admin). Admin accounts are not staff: they exist only through --create-admin.
public static class StaffPolicy
{
    public static IReadOnlyList<RoleCode> StaffRoles { get; } =
        [RoleCode.StoreOwner, RoleCode.SalesStaff, RoleCode.DeliveryStaff];

    public static bool IsStaffRole(RoleCode role) => StaffRoles.Contains(role);

    public static bool CanManage(RoleCode actor, RoleCode target) => actor switch
    {
        RoleCode.Admin => IsStaffRole(target),
        RoleCode.StoreOwner => target is RoleCode.SalesStaff or RoleCode.DeliveryStaff,
        _ => false
    };

    // Roles allowed to call the staff API at all.
    public static bool CanUseStaffApi(RoleCode actor) => actor is RoleCode.Admin or RoleCode.StoreOwner;
}
