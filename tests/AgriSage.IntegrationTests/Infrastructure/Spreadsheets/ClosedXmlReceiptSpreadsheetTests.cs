using System.IO.Compression;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.GoodsReceipts.Import;
using AgriSage.Infrastructure.Spreadsheets;
using ClosedXML.Excel;

namespace AgriSage.IntegrationTests.Infrastructure.Spreadsheets;

// The goods receipt template and its reader (F4.3), with real .xlsx files built by ClosedXML, no database.
public class ClosedXmlReceiptSpreadsheetTests
{
    private readonly ClosedXmlReceiptSpreadsheet _spreadsheet = new();

    private static MemoryStream Workbook(Action<XLWorkbook> fill)
    {
        using var workbook = new XLWorkbook();
        fill(workbook);
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static void Header(IXLWorksheet sheet, params string[] names)
    {
        for (var index = 0; index < names.Length; index++)
        {
            sheet.Cell(1, index + 1).Value = names[index];
        }
    }

    private static string FileError(ValidationException exception) => Assert.Single(exception.Errors["file"]);

    [Fact]
    public void The_template_has_the_receipt_header_the_products_and_a_guide_and_reads_as_empty()
    {
        var bytes = _spreadsheet.CreateTemplate(
            [new ReceiptTemplateProduct("SKU-001", "Phân NPK 16-16-8", "Bao 25kg", "8935001", 25, "Kg", true, false)]);

        using (var workbook = new XLWorkbook(new MemoryStream(bytes)))
        {
            Assert.Equal(
                [ClosedXmlReceiptSpreadsheet.ReceiptSheet, ClosedXmlReceiptSpreadsheet.ProductsSheet, ClosedXmlReceiptSpreadsheet.GuideSheet],
                workbook.Worksheets.Select(sheet => sheet.Name));
            var receipt = workbook.Worksheet(ClosedXmlReceiptSpreadsheet.ReceiptSheet);
            Assert.Equal(ReceiptSheetColumns.All, Enumerable.Range(1, 7).Select(column => receipt.Cell(1, column).GetString()));
            var products = workbook.Worksheet(ClosedXmlReceiptSpreadsheet.ProductsSheet);
            Assert.Equal("SKU-001", products.Cell(2, 1).GetString());
            Assert.Equal("Bao 25kg", products.Cell(2, 3).GetString());
            Assert.Equal("8935001", products.Cell(2, 4).GetString());
            Assert.Equal("YES", products.Cell(2, 7).GetString());
            Assert.Equal("NO", products.Cell(2, 8).GetString());
        }

        Assert.Empty(_spreadsheet.Read(new MemoryStream(bytes), 500));
    }

    [Fact]
    public void Rows_are_read_as_text_whatever_the_column_order_cell_types_and_formulas()
    {
        using var file = Workbook(workbook =>
        {
            var sheet = workbook.AddWorksheet("Receipt");
            Header(sheet, "Unit Cost", "sku", "Note", "PACKAGING", "quantity", "Lot_Number", "Expiry Date", "ManufactureDate");
            sheet.Cell(2, 1).Value = 240000.5;
            sheet.Cell(2, 2).Value = "  0012 ";
            sheet.Cell(2, 3).Value = "ignored column";
            sheet.Cell(2, 4).Value = "Bao 25kg";
            sheet.Cell(2, 5).FormulaA1 = "2*5";
            sheet.Cell(2, 6).Value = 20261001;
            sheet.Cell(2, 7).Value = new DateTime(2027, 1, 31);
            sheet.Cell(2, 8).Value = "15/09/2026";
            // Row 3 is empty and skipped; row 4 only has a value in an unknown column and is skipped too.
            sheet.Cell(4, 3).Value = "just a note";
            sheet.Cell(5, 2).Value = "SKU-2";
            sheet.Cell(5, 1).Value = 0.1 + 0.2;
        });

        var rows = _spreadsheet.Read(file, 500);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new ReceiptSheetRow(2, "0012", "Bao 25kg", "10", "240000.5", "20261001", "2027-01-31", "15/09/2026"), rows[0]);
        Assert.Equal(5, rows[1].RowNumber);
        Assert.Equal("SKU-2", rows[1].Sku);
        Assert.Equal("0.3", rows[1].UnitCost);
        Assert.Null(rows[1].Quantity);
    }

    [Fact]
    public void The_sheet_named_Receipt_is_used_even_when_it_is_not_the_first_one()
    {
        using var file = Workbook(workbook =>
        {
            Header(workbook.AddWorksheet("Notes"), "Anything");
            var receipt = workbook.AddWorksheet("Receipt");
            Header(receipt, "SKU", "Packaging", "Quantity", "UnitCost");
            receipt.Cell(2, 1).Value = "SKU-9";
        });

        Assert.Equal("SKU-9", Assert.Single(_spreadsheet.Read(file, 500)).Sku);
    }

    [Fact]
    public void A_missing_required_column_is_named()
    {
        using var file = Workbook(workbook => Header(workbook.AddWorksheet("Receipt"), "SKU", "Quantity"));

        var message = FileError(Assert.Throws<ValidationException>(() => _spreadsheet.Read(file, 500)));

        Assert.Contains("Packaging", message);
        Assert.Contains("UnitCost", message);
    }

    [Fact]
    public void More_rows_than_allowed_are_refused()
    {
        using var file = Workbook(workbook =>
        {
            var sheet = workbook.AddWorksheet("Receipt");
            Header(sheet, ReceiptSheetColumns.All.ToArray());
            for (var row = 2; row <= 5; row++)
            {
                sheet.Cell(row, 1).Value = $"SKU-{row}";
            }
        });

        Assert.Contains("more than 3 rows", FileError(Assert.Throws<ValidationException>(() => _spreadsheet.Read(file, 3))));
    }

    [Fact]
    public void Files_that_are_not_xlsx_workbooks_are_refused_without_details()
    {
        var notZip = new MemoryStream("SKU,Packaging\nA,B"u8.ToArray());
        Assert.Contains("not a valid .xlsx", FileError(Assert.Throws<ValidationException>(() => _spreadsheet.Read(notZip, 500))));

        var zipButNotWorkbook = new MemoryStream();
        using (var zip = new ZipArchive(zipButNotWorkbook, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("hello.txt").Open());
            writer.Write("hello");
        }

        zipButNotWorkbook.Position = 0;
        Assert.Contains(
            "not a valid .xlsx", FileError(Assert.Throws<ValidationException>(() => _spreadsheet.Read(zipButNotWorkbook, 500))));
    }
}
