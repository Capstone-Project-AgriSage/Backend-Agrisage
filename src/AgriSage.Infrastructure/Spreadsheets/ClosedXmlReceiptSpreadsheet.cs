using System.Globalization;
using System.IO.Compression;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.GoodsReceipts.Import;
using ClosedXML.Excel;

namespace AgriSage.Infrastructure.Spreadsheets;

// The goods receipt template with ClosedXML (decision F-D7). Sheet "Receipt" = lines (header in row 1), "Products" =
// reference list of purchasable packagings, "Guide" = instructions. Reading accepts the columns in any order, uses the
// value of formula cells, and turns every cell into text for the Application (ReceiptSheetRow).
public sealed class ClosedXmlReceiptSpreadsheet : IReceiptSpreadsheet
{
    public const string ReceiptSheet = "Receipt";
    public const string ProductsSheet = "Products";
    public const string GuideSheet = "Guide";

    // A 2 MB .xlsx is a zip; the declared uncompressed sizes are checked before ClosedXML expands it (zip bomb guard
    // for honest headers; the endpoint is staff-only and rate limited).
    private const long MaxUncompressedBytes = 50L * 1024 * 1024;
    private const int MaxZipEntries = 500;

    private static readonly string[] ProductColumns =
        ["SKU", "ProductName", "Packaging", "Barcode", "ConversionToBase", "BaseUnit", "RequiresLotNumber", "RequiresExpiryDate"];

    public IReadOnlyList<ReceiptSheetRow> Read(Stream content, int maxRows)
    {
        EnsureReasonableZip(content);

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(content);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw FileError("The file is not a valid .xlsx workbook. Download the template and fill it in.");
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.TryGetWorksheet(ReceiptSheet, out var named) ? named : workbook.Worksheet(1);
            var columns = MapHeader(sheet);
            var missing = ReceiptSheetColumns.Required.Where(column => !columns.ContainsKey(column)).ToList();
            if (missing.Count > 0)
            {
                throw FileError($"The sheet \"{sheet.Name}\" misses the column(s): {string.Join(", ", missing)} (header in row 1).");
            }

            var rows = new List<ReceiptSheetRow>();
            foreach (var row in sheet.RowsUsed().Where(row => row.RowNumber() > 1))
            {
                string? Text(string column) => columns.TryGetValue(column, out var index) ? CellText(row.Cell(index)) : null;

                var line = new ReceiptSheetRow(
                    row.RowNumber(),
                    Text(ReceiptSheetColumns.Sku),
                    Text(ReceiptSheetColumns.Packaging),
                    Text(ReceiptSheetColumns.Quantity),
                    Text(ReceiptSheetColumns.UnitCost),
                    Text(ReceiptSheetColumns.LotNumber),
                    Text(ReceiptSheetColumns.ExpiryDate),
                    Text(ReceiptSheetColumns.ManufactureDate));

                if (line is { Sku: null, Packaging: null, Quantity: null, UnitCost: null, LotNumber: null, ExpiryDate: null, ManufactureDate: null })
                {
                    continue;
                }

                if (rows.Count == maxRows)
                {
                    throw FileError($"The file has more than {maxRows} rows; split it into several receipts.");
                }

                rows.Add(line);
            }

            return rows;
        }
    }

    public byte[] CreateTemplate(IReadOnlyList<ReceiptTemplateProduct> products)
    {
        using var workbook = new XLWorkbook();

        var receipt = workbook.AddWorksheet(ReceiptSheet);
        WriteHeader(receipt, ReceiptSheetColumns.All);
        SetColumn(receipt, 1, 18, "@");           // SKU as text (keeps leading zeros)
        SetColumn(receipt, 2, 18, "@");           // Packaging
        SetColumn(receipt, 3, 10, "0");           // Quantity
        SetColumn(receipt, 4, 16, "#,##0.00");    // UnitCost
        SetColumn(receipt, 5, 16, "@");           // LotNumber
        SetColumn(receipt, 6, 14, "yyyy-mm-dd");  // ExpiryDate
        SetColumn(receipt, 7, 16, "yyyy-mm-dd");  // ManufactureDate

        var reference = workbook.AddWorksheet(ProductsSheet);
        WriteHeader(reference, ProductColumns);
        SetColumn(reference, 1, 18, "@");
        SetColumn(reference, 2, 40, "@");
        SetColumn(reference, 3, 18, "@");
        SetColumn(reference, 4, 18, "@");
        SetColumn(reference, 5, 16, "0");
        SetColumn(reference, 6, 12, "@");
        SetColumn(reference, 7, 18, "@");
        SetColumn(reference, 8, 18, "@");
        for (var index = 0; index < products.Count; index++)
        {
            var product = products[index];
            var row = index + 2;
            reference.Cell(row, 1).Value = product.Sku;
            reference.Cell(row, 2).Value = product.ProductName;
            reference.Cell(row, 3).Value = product.Packaging;
            reference.Cell(row, 4).Value = product.Barcode ?? "";
            reference.Cell(row, 5).Value = product.ConversionToBase;
            reference.Cell(row, 6).Value = product.BaseUnit;
            reference.Cell(row, 7).Value = product.RequiresLotTracking ? "YES" : "NO";
            reference.Cell(row, 8).Value = product.RequiresExpiryDate ? "YES" : "NO";
        }

        var guide = workbook.AddWorksheet(GuideSheet);
        string[] lines =
        [
            "HƯỚNG DẪN NHẬP KHO TỪ EXCEL (AgriSage)",
            "1. Mỗi dòng của sheet Receipt là một dòng hàng nhập; dòng 1 là tiêu đề, không đổi tên cột.",
            "2. SKU và Packaging: chép từ sheet Products (Packaging = tên quy cách hoặc mã vạch Barcode). Chỉ quy cách nhập hàng.",
            "3. Quantity: số nguyên ≥ 1, tính theo quy cách (bao, thùng...). UnitCost: giá nhập của 1 quy cách, ≥ 0, tối đa 2 số lẻ, dùng dấu chấm.",
            "4. LotNumber: bắt buộc khi RequiresLotNumber = YES. ExpiryDate: bắt buộc khi RequiresExpiryDate = YES, không nhập hàng đã hết hạn.",
            "5. Ngày: dùng ô định dạng ngày, hoặc gõ yyyy-MM-dd hay dd/MM/yyyy. ManufactureDate không được sau hôm nay hay sau ExpiryDate.",
            "6. Tối đa 500 dòng, file ≤ 2 MB. Nhà cung cấp, số và ngày hoá đơn được chọn trên màn hình khi tải file lên.",
            "7. Xem trước (preview) để thấy lỗi từng ô; khi nhập, chỉ cần một dòng lỗi là không dòng nào được lưu.",
            "8. Nhập xong được một phiếu NHÁP: kiểm tra, sửa nếu cần rồi bấm Xác nhận để cộng tồn kho."
        ];
        for (var index = 0; index < lines.Length; index++)
        {
            guide.Cell(index + 1, 1).Value = lines[index];
        }

        guide.Cell(1, 1).Style.Font.Bold = true;
        guide.Column(1).Width = 120;

        receipt.SetTabActive();
        using var output = new MemoryStream();
        workbook.SaveAs(output);

        return output.ToArray();
    }

    private static void EnsureReasonableZip(Stream content)
    {
        try
        {
            using var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count > MaxZipEntries || zip.Entries.Sum(entry => entry.Length) > MaxUncompressedBytes)
            {
                throw FileError("The workbook is too large once unpacked.");
            }
        }
        catch (InvalidDataException)
        {
            throw FileError("The file is not a valid .xlsx workbook. Download the template and fill it in.");
        }

        content.Position = 0;
    }

    // Header name → column number, matching ignoring case, spaces, underscores and dashes; the first occurrence wins.
    private static Dictionary<string, int> MapHeader(IXLWorksheet sheet)
    {
        var known = ReceiptSheetColumns.All.ToDictionary(Normalize, column => column);
        var columns = new Dictionary<string, int>();

        foreach (var cell in sheet.Row(1).CellsUsed())
        {
            if (known.TryGetValue(Normalize(CellText(cell) ?? ""), out var column))
            {
                columns.TryAdd(column, cell.Address.ColumnNumber);
            }
        }

        return columns;
    }

    private static string Normalize(string header) =>
        new(header.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string? CellText(IXLCell cell)
    {
        // A formula is evaluated by ClosedXML (files saved by Excel also carry the cached result, used as fallback).
        XLCellValue value;
        try
        {
            value = cell.Value;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            try
            {
                value = cell.CachedValue;
            }
            catch (Exception inner) when (inner is not OperationCanceledException)
            {
                return "#ERROR";
            }
        }

        switch (value.Type)
        {
            case XLDataType.Blank:
                return null;
            case XLDataType.Text:
                var text = value.GetText().Trim();
                return text.Length == 0 ? null : text;
            case XLDataType.Number:
                var number = value.GetNumber();
                // decimal(double) keeps 15 significant digits, so 0.1 + 0.2 reads as 0.3.
                return Math.Abs(number) < 7.9e27
                    ? new decimal(number).ToString(CultureInfo.InvariantCulture)
                    : number.ToString("R", CultureInfo.InvariantCulture);
            case XLDataType.DateTime:
                return DateOnly.FromDateTime(value.GetDateTime()).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            case XLDataType.Boolean:
                return value.GetBoolean() ? "TRUE" : "FALSE";
            case XLDataType.TimeSpan:
                return value.GetTimeSpan().ToString("c", CultureInfo.InvariantCulture);
            default:
                return "#ERROR";
        }
    }

    private static void WriteHeader(IXLWorksheet sheet, IReadOnlyList<string> names)
    {
        for (var index = 0; index < names.Count; index++)
        {
            sheet.Cell(1, index + 1).Value = names[index];
        }

        var header = sheet.Range(1, 1, 1, names.Count).Style;
        header.Font.Bold = true;
        header.Fill.BackgroundColor = XLColor.FromHtml("#E2EFDA");
        sheet.SheetView.FreezeRows(1);
    }

    private static void SetColumn(IXLWorksheet sheet, int column, double width, string format)
    {
        sheet.Column(column).Width = width;
        sheet.Column(column).Style.NumberFormat.Format = format;
        sheet.Cell(1, column).Style.NumberFormat.Format = "@";
    }

    private static ValidationException FileError(string message) =>
        new(new Dictionary<string, string[]> { ["file"] = [message] });
}
