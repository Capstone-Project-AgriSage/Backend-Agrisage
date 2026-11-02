using AgriSage.Application.Features.Reports;
using AgriSage.Infrastructure.Spreadsheets;
using ClosedXML.Excel;

namespace AgriSage.IntegrationTests.Infrastructure.Inventory;

public class InventoryReportExportTests
{
    [Fact]
    public void Spreadsheet_preserves_quantities_and_treats_formula_like_product_names_as_text()
    {
        var amount = new InventoryMovementAmount(0, 0);
        var row = new InventoryMovementReportRow(Guid.NewGuid(), "001", "=1+1", "CHAI", 10, 100,
            new(5, 50), amount, amount, new(-3, -30), amount, amount, 12, 120);
        var report = new InventoryMovementReportResponse(new(2026, 11, 1), new(2026, 11, 2), [row],
            new(100, 50, 0, 0, -30, 0, 0, 120));
        var bytes = new ClosedXmlInventoryReportSpreadsheet().Export(report);
        using var book = new XLWorkbook(new MemoryStream(bytes));
        var sheet = book.Worksheet(1);
        Assert.Equal("001", sheet.Cell(4, 1).GetString());
        Assert.Equal("=1+1", sheet.Cell(4, 2).GetString());
        Assert.False(sheet.Cell(4, 2).HasFormula);
        Assert.Equal(12, sheet.Cell(4, 12).GetValue<int>());
        Assert.Equal(120, sheet.Cell(4, 13).GetValue<decimal>());
    }
}
