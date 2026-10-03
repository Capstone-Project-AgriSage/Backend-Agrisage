namespace AgriSage.Application.Features.GoodsReceipts.Import;

// Reads and writes the AgriSage goods receipt template (.xlsx, design §22 "Excel workflow"). Implemented in
// Infrastructure with ClosedXML (decision F-D7); the Application never sees the spreadsheet library.
public interface IReceiptSpreadsheet
{
    // Non-empty data rows of the receipt sheet, in sheet order. Throws ValidationException (key "file") when the
    // stream is not a readable .xlsx workbook, a required column is missing or there are more than maxRows rows.
    IReadOnlyList<ReceiptSheetRow> Read(Stream content, int maxRows);

    // Empty template: the receipt sheet with its header row, a guide and the given products for reference.
    byte[] CreateTemplate(IReadOnlyList<ReceiptTemplateProduct> products);
}

// Header names of the receipt sheet (matched ignoring case, spaces and underscores; any order; extra columns ignored).
public static class ReceiptSheetColumns
{
    public const string Sku = "SKU";
    public const string Packaging = "Packaging";
    public const string Quantity = "Quantity";
    public const string UnitCost = "UnitCost";
    public const string LotNumber = "LotNumber";
    public const string ExpiryDate = "ExpiryDate";
    public const string ManufactureDate = "ManufactureDate";

    // Pseudo-column of errors about the whole row.
    public const string Row = "Row";

    public static readonly IReadOnlyList<string> All = [Sku, Packaging, Quantity, UnitCost, LotNumber, ExpiryDate, ManufactureDate];

    public static readonly IReadOnlyList<string> Required = [Sku, Packaging, Quantity, UnitCost];
}

// One data row as text: numbers in invariant culture, cells formatted as dates as yyyy-MM-dd, blanks as null.
// RowNumber is the Excel row number (the header is row 1).
public sealed record ReceiptSheetRow(
    int RowNumber,
    string? Sku,
    string? Packaging,
    string? Quantity,
    string? UnitCost,
    string? LotNumber,
    string? ExpiryDate,
    string? ManufactureDate);

// A purchasable packaging of a store product, listed in the template so staff can copy SKU and Packaging.
public sealed record ReceiptTemplateProduct(
    string Sku,
    string ProductName,
    string Packaging,
    string? Barcode,
    long ConversionToBase,
    string BaseUnit,
    bool RequiresLotTracking,
    bool RequiresExpiryDate);
