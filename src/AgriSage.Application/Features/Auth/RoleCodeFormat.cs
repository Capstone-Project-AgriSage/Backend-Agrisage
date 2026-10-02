using System.Text.RegularExpressions;
using AgriSage.Domain.Features.Identity.Enums;

namespace AgriSage.Application.Features.Auth;

// Role code as stored in the database and carried in the JWT `role` claim: Farmer → FARMER, StoreOwner → STORE_OWNER.
public static partial class RoleCodeFormat
{
    public static string ToText(RoleCode role) => WordBoundary().Replace(role.ToString(), "_$1").ToUpperInvariant();

    public static bool TryParse(string? text, out RoleCode role)
    {
        foreach (var candidate in Enum.GetValues<RoleCode>())
        {
            if (string.Equals(ToText(candidate), text?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                role = candidate;
                return true;
            }
        }

        role = default;
        return false;
    }

    [GeneratedRegex("(?<=[a-z0-9])([A-Z])")]
    private static partial Regex WordBoundary();
}
