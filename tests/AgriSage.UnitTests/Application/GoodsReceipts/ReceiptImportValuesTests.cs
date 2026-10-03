using AgriSage.Application.Features.GoodsReceipts;
using AgriSage.Application.Features.GoodsReceipts.Import;
using AgriSage.Domain.Features.Products.Enums;
using static AgriSage.UnitTests.Domain.Features.Products.CatalogTestData;

namespace AgriSage.UnitTests.Application.GoodsReceipts;

// Cell values of the goods receipt Excel import (F4.3) and the column each line rule points at.
public class ReceiptImportValuesTests
{
    private static readonly DateOnly Today = new(2026, 10, 5);

    [Theory]
    [InlineData("10", 10)]
    [InlineData("10.0", 10)]
    [InlineData(" 3 ", 3)]
    public void Quantities_are_whole_packages(string text, long expected)
    {
        Assert.True(ReceiptImportValues.TryParseQuantity(text, out var quantity));
        Assert.Equal(expected, quantity);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("1,000")]
    [InlineData("1.000.000")]
    [InlineData("ten")]
    public void Zero_negative_fractional_or_formatted_quantities_are_refused(string text)
    {
        Assert.False(ReceiptImportValues.TryParseQuantity(text, out _));
    }

    [Theory]
    [InlineData("240000", 240000)]
    [InlineData("240000.5", 240000.5)]
    [InlineData("0", 0)]
    [InlineData("12.34", 12.34)]
    public void Unit_costs_are_money_with_at_most_two_decimals(string text, decimal expected)
    {
        Assert.True(ReceiptImportValues.TryParseMoney(text, out var amount));
        Assert.Equal(expected, amount);
    }

    [Theory]
    [InlineData("1.234")]
    [InlineData("-1")]
    [InlineData("1,5")]
    [InlineData("240.000,50")]
    [InlineData("free")]
    public void Unit_costs_are_never_rounded_or_guessed(string text)
    {
        Assert.False(ReceiptImportValues.TryParseMoney(text, out _));
    }

    [Theory]
    [InlineData("2027-01-31", 2027, 1, 31)]
    [InlineData("31/01/2027", 2027, 1, 31)]
    [InlineData("1/2/2027", 2027, 2, 1)]
    public void Dates_are_iso_or_vietnamese_day_month_year(string text, int year, int month, int day)
    {
        Assert.True(ReceiptImportValues.TryParseDate(text, out var date));
        Assert.Equal(new DateOnly(year, month, day), date);
    }

    [Theory]
    [InlineData("2027/01/31")]
    [InlineData("31-01-2027")]
    [InlineData("01/31/2027")]
    [InlineData("soon")]
    public void Other_date_texts_are_refused(string text)
    {
        Assert.False(ReceiptImportValues.TryParseDate(text, out _));
    }

    [Fact]
    public void Only_the_file_name_is_kept_and_it_is_capped()
    {
        Assert.Equal("nhap-kho.xlsx", ReceiptImportValues.FileName(@"C:\fakepath\nhap-kho.xlsx"));
        Assert.Equal("a.xlsx", ReceiptImportValues.FileName("folder/a.xlsx"));
        Assert.Equal(255, ReceiptImportValues.FileName(new string('x', 300) + ".xlsx").Length);
    }

    [Fact]
    public void Each_rule_violation_names_the_input_it_is_about()
    {
        var (product, bottle, box) = CreateProductWithPackagings();

        Assert.Equal(ReceiptItemField.Packaging, ReceiptItemRules.Check(product, bottle, "L1", null, Today.AddYears(1), Today)!.Field);
        Assert.Equal(ReceiptItemField.LotNumber, ReceiptItemRules.Check(product, box, null, null, Today.AddYears(1), Today)!.Field);
        Assert.Equal(ReceiptItemField.ExpiryDate, ReceiptItemRules.Check(product, box, "L1", null, null, Today)!.Field);
        Assert.Equal(ReceiptItemField.ExpiryDate, ReceiptItemRules.Check(product, box, "L1", null, Today.AddDays(-1), Today)!.Field);
        Assert.Equal(
            ReceiptItemField.ManufacturingDate,
            ReceiptItemRules.Check(product, box, "L1", Today.AddDays(1), Today.AddYears(1), Today)!.Field);
        Assert.Null(ReceiptItemRules.Check(product, box, "L1", Today, Today.AddYears(1), Today));

        product.ChangeStatus(ProductStatus.Discontinued);
        Assert.Equal(ReceiptItemField.Product, ReceiptItemRules.Check(product, box, "L1", null, Today.AddYears(1), Today)!.Field);
    }
}
