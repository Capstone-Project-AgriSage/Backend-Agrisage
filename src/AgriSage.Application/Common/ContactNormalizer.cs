using System.Text.RegularExpressions;

namespace AgriSage.Application.Common;

// One canonical form for contact data, because the unique indexes compare the stored text
// (database design §2): phone numbers are Vietnamese mobile numbers stored as 0xxxxxxxxx; emails are lower-case.
public static partial class ContactNormalizer
{
    public const int MaxEmailLength = 255;

    public static bool TryNormalizePhone(string? input, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var digits = Separators().Replace(input, string.Empty);
        if (digits.StartsWith("+84", StringComparison.Ordinal))
        {
            digits = "0" + digits[3..];
        }
        else if (digits.StartsWith("84", StringComparison.Ordinal) && digits.Length == 11)
        {
            digits = "0" + digits[2..];
        }

        if (!MobileNumber().IsMatch(digits))
        {
            return false;
        }

        normalized = digits;
        return true;
    }

    public static string? NormalizeEmail(string? input) =>
        string.IsNullOrWhiteSpace(input) ? null : input.Trim().ToLowerInvariant();

    [GeneratedRegex(@"[\s.\-()]")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^0(3|5|7|8|9)\d{8}$")]
    private static partial Regex MobileNumber();
}
