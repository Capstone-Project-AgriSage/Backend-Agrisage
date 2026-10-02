using System.Text.RegularExpressions;

namespace AgriSage.Application.Common;

// Enum values as written in the API and the database: ExcelTemplate → EXCEL_TEMPLATE, StockIn → STOCK_IN.
public static partial class EnumText
{
    public static string Format(Enum value) => WordBoundary().Replace(value.ToString(), "_$1").ToUpperInvariant();

    public static bool TryParse<TEnum>(string? text, out TEnum value)
        where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(Format(candidate), text?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }

    [GeneratedRegex("(?<=[a-z0-9])([A-Z])")]
    private static partial Regex WordBoundary();
}
