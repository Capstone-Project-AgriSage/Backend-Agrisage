using AgriSage.Application.Features.Reports;
using ClosedXML.Excel;

namespace AgriSage.Infrastructure.Spreadsheets;

public sealed class ClosedXmlInventoryReportSpreadsheet : IInventoryReportSpreadsheet
{
    public byte[] Export(InventoryMovementReportResponse report)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Nhap-xuat-ton");
        sheet.Cell(1, 1).Value = $"Nhập–xuất–tồn {report.FromDate:dd/MM/yyyy} – {report.ToDate:dd/MM/yyyy}";
        string[] headers = ["SKU", "Sản phẩm", "Đơn vị", "Tồn đầu", "Giá trị đầu", "Nhập", "Khách trả",
            "Điều chỉnh tăng", "Xuất bán", "Điều chỉnh giảm", "Đảo giao dịch", "Tồn cuối", "Giá trị cuối"];
        for (var c = 0; c < headers.Length; c++) sheet.Cell(3, c + 1).Value = headers[c];
        var row = 4;
        foreach (var r in report.Rows) {
            // Text uses Value, never FormulaA1: supplier/product names cannot inject spreadsheet formulas.
            sheet.Cell(row, 1).Value = r.Sku; sheet.Cell(row, 2).Value = r.ProductName; sheet.Cell(row, 3).Value = r.BaseUnit;
            decimal[] values = [r.OpeningQuantity, r.OpeningValue, r.StockIn.Quantity, r.ReturnIn.Quantity,
                r.AdjustmentIn.Quantity, r.Sale.Quantity, r.AdjustmentOut.Quantity, r.Reversal.Quantity,
                r.ClosingQuantity, r.ClosingValue];
            for (var c = 0; c < values.Length; c++) sheet.Cell(row, c + 4).Value = values[c];
            row++;
        }
        sheet.Range(3, 1, Math.Max(3, row - 1), headers.Length).SetAutoFilter();
        sheet.Row(3).Style.Font.Bold = true;
        sheet.Row(3).Style.Fill.BackgroundColor = XLColor.LightGreen;
        sheet.SheetView.FreezeRows(3);
        sheet.Columns(1, headers.Length).AdjustToContents(8, 45);
        if (row > 4) sheet.Range(4, 4, row - 1, headers.Length).Style.NumberFormat.Format = "#,##0.##";
        using var stream = new MemoryStream(); book.SaveAs(stream); return stream.ToArray();
    }
}
