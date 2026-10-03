using System.Globalization;

namespace AgriSage.Application.Features.GoodsReceipts.Import;

// Limits of the goods receipt Excel import (FLOW_4 §5).
public static class ReceiptImportRules
{
    public const long MaxFileBytes = 2 * 1024 * 1024;

    public const int MaxRows = 500;

    public const int MaxFileNameLength = 255;

    public const int MaxLotNumberLength = 100;

    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public const string TemplateFileName = "AgriSage-goods-receipt-template.xlsx";
}

// Parses the text of one sheet cell. Numbers come from the reader in invariant culture; a value typed as text must
// use a dot for decimals and no thousands separator (a Vietnamese "1.000" would otherwise silently become 1).
public static class ReceiptImportValues
{
    private const NumberStyles Plain =
        NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowDecimalPoint;

    // Excel dates arrive as yyyy-MM-dd; dd/MM/yyyy (Vietnamese order) is accepted for dates typed as text.
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy"];

    // Whole packaging units, at least 1.
    public static bool TryParseQuantity(string text, out long quantity)
    {
        quantity = 0;
        if (!decimal.TryParse(text, Plain, CultureInfo.InvariantCulture, out var value)
            || value != decimal.Truncate(value) || value < 1 || value > long.MaxValue)
        {
            return false;
        }

        quantity = (long)value;
        return true;
    }

    // Money: ≥ 0 with at most 2 decimals, never rounded (coding rule #61).
    public static bool TryParseMoney(string text, out decimal amount) =>
        decimal.TryParse(text, Plain, CultureInfo.InvariantCulture, out amount)
        && amount == decimal.Round(amount, 2);

    public static bool TryParseDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    // The name only (some browsers send a full client path), at most 255 characters.
    public static string FileName(string fileName)
    {
        var name = fileName[(fileName.LastIndexOfAny(['/', '\\']) + 1)..].Trim();

        return name.Length <= ReceiptImportRules.MaxFileNameLength ? name : name[..ReceiptImportRules.MaxFileNameLength];
    }
}
