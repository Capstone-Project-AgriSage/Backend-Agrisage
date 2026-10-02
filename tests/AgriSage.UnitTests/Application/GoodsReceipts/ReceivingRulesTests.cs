using AgriSage.Application.Common;
using AgriSage.Application.Features.GoodsReceipts;
using AgriSage.Application.Features.Inventory;
using AgriSage.Application.Features.Suppliers;
using AgriSage.Domain.Features.GoodsReceipts.Enums;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Products.Enums;
using static AgriSage.UnitTests.Domain.Features.Products.CatalogTestData;

namespace AgriSage.UnitTests.Application.GoodsReceipts;

public class ReceivingRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 5);

    // Lot and expiry are required (defaults); the box is a purchase unit, the bottle is not.
    private static (Product Product, ProductPackaging Bottle, ProductPackaging Box) Setup() => CreateProductWithPackagings();

    private static string? Check(
        Product product, ProductPackaging packaging, string? lot = "LOT-1", DateOnly? mfg = null, DateOnly? expiry = null) =>
        ReceiptItemRules.GetViolation(product, packaging, lot, mfg, expiry ?? Today.AddMonths(12), Today);

    [Fact]
    public void A_complete_line_for_a_purchase_packaging_is_accepted()
    {
        var (product, _, box) = Setup();

        Assert.Null(Check(product, box, mfg: Today.AddMonths(-1)));
        Assert.Null(Check(product, box, expiry: Today));
    }

    [Fact]
    public void Packaging_must_be_a_purchase_unit_and_active()
    {
        var (product, bottle, box) = Setup();

        Assert.NotNull(Check(product, bottle));
        box.ChangeStatus(PackagingStatus.Inactive);
        Assert.NotNull(Check(product, box));
    }

    [Fact]
    public void Discontinued_products_cannot_be_received_but_inactive_ones_can()
    {
        var (product, _, box) = Setup();

        product.ChangeStatus(ProductStatus.Inactive);
        Assert.Null(Check(product, box));
        product.ChangeStatus(ProductStatus.Discontinued);
        Assert.NotNull(Check(product, box));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Lot_tracked_products_need_a_lot_number(string? lot)
    {
        var (product, _, box) = Setup();

        Assert.NotNull(Check(product, box, lot));
    }

    [Fact]
    public void Products_that_require_an_expiry_date_need_one()
    {
        var (product, _, box) = Setup();

        Assert.NotNull(ReceiptItemRules.GetViolation(product, box, "LOT-1", null, null, Today));
    }

    [Fact]
    public void Products_without_lot_and_expiry_tracking_need_neither()
    {
        var product = new Product(Guid.NewGuid(), "SKU-X", "Tool", requiresLotTracking: false, requiresExpiryDate: false);
        var packaging = product.AddPackaging(Guid.NewGuid(), 1, true, true, true, PackagingStatus.Active);

        Assert.Null(ReceiptItemRules.GetViolation(product, packaging, null, null, null, Today));
    }

    [Fact]
    public void Expired_goods_and_impossible_dates_are_rejected()
    {
        var (product, _, box) = Setup();

        Assert.NotNull(Check(product, box, expiry: Today.AddDays(-1)));
        Assert.NotNull(Check(product, box, mfg: Today.AddDays(1)));
        Assert.NotNull(Check(product, box, mfg: Today.AddMonths(6), expiry: Today.AddMonths(3)));
    }

    [Fact]
    public void Document_numbers_follow_the_agreed_format_and_sequence()
    {
        Assert.Equal("GR-20261005-0007", DocumentNumbers.Format("GR", Today, 7));
        Assert.Equal("SM-20261005-0123", DocumentNumbers.Format("SM", Today, 123));

        Assert.Equal(7, DocumentNumbers.SequenceOf("GR-20261005-0007", "GR", Today));
        Assert.Equal(0, DocumentNumbers.SequenceOf("GR-20261004-0007", "GR", Today));
        Assert.Equal(0, DocumentNumbers.SequenceOf("SM-20261005-0007", "GR", Today));
        Assert.Equal(0, DocumentNumbers.SequenceOf("GR-20261005-abcd", "GR", Today));
        Assert.Equal(0, DocumentNumbers.SequenceOf("custom", "GR", Today));
    }

    [Fact]
    public void Business_day_is_the_vietnam_day()
    {
        // 18:00 UTC is already 01:00 of the next day in Vietnam.
        var late = new DateTimeOffset(2026, 10, 5, 18, 0, 0, TimeSpan.Zero);
        var early = new DateTimeOffset(2026, 10, 5, 16, 59, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 10, 6), BusinessCalendar.Today(late));
        Assert.Equal(new DateOnly(2026, 10, 5), BusinessCalendar.Today(early));
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 17, 0, 0, TimeSpan.Zero), BusinessCalendar.StartOfDay(new DateOnly(2026, 10, 6)));
    }

    [Fact]
    public void Enum_text_uses_upper_snake_case_and_parses_back()
    {
        Assert.Equal("EXCEL_TEMPLATE", EnumText.Format(GoodsReceiptSourceType.ExcelTemplate));
        Assert.Equal("STOCK_IN", EnumText.Format(StockMovementType.StockIn));
        Assert.Equal("DRAFT", EnumText.Format(GoodsReceiptStatus.Draft));

        foreach (var type in Enum.GetValues<StockMovementType>())
        {
            Assert.True(EnumText.TryParse<StockMovementType>(EnumText.Format(type).ToLowerInvariant(), out var parsed));
            Assert.Equal(type, parsed);
        }

        Assert.False(EnumText.TryParse<StockMovementType>("NOPE", out _));
        Assert.False(EnumText.TryParse<StockMovementType>(null, out _));
    }

    private static GoodsReceiptItemRequest Line(long quantity = 1, decimal cost = 100m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), quantity, cost);

    [Theory]
    [InlineData(100, true)]
    [InlineData(100.5, true)]
    [InlineData(100.55, true)]
    [InlineData(0, true)]
    [InlineData(-0.01, false)]
    public void Receipt_line_cost_must_not_be_negative(double cost, bool valid) =>
        Assert.Equal(valid, new GoodsReceiptItemRequestValidator().Validate(Line(cost: (decimal)cost)).IsValid);

    [Fact]
    public void Receipt_line_cost_with_more_than_two_decimals_is_rejected()
    {
        var validator = new GoodsReceiptItemRequestValidator();

        Assert.False(validator.Validate(Line(cost: 0.005m)).IsValid);
        Assert.False(validator.Validate(Line(cost: 10.123m)).IsValid);
        Assert.True(validator.Validate(Line(cost: 10.50m)).IsValid);
    }

    [Fact]
    public void Receipt_line_needs_a_positive_quantity_and_ids()
    {
        var validator = new GoodsReceiptItemRequestValidator();

        Assert.False(validator.Validate(Line(quantity: 0)).IsValid);
        Assert.False(validator.Validate(Line(quantity: -3)).IsValid);
        Assert.False(validator.Validate(new GoodsReceiptItemRequest(Guid.Empty, Guid.NewGuid(), 1, 1m)).IsValid);
        Assert.False(validator.Validate(Line() with { SupplierLotNumber = new string('x', 101) }).IsValid);
    }

    [Fact]
    public void Creating_a_receipt_validates_its_lines()
    {
        var validator = new CreateGoodsReceiptRequestValidator();

        Assert.True(validator.Validate(new CreateGoodsReceiptRequest(Guid.NewGuid())).IsValid);
        Assert.True(validator.Validate(new CreateGoodsReceiptRequest(Guid.NewGuid(), Items: [Line()])).IsValid);
        Assert.False(validator.Validate(new CreateGoodsReceiptRequest(Guid.Empty)).IsValid);
        Assert.False(validator.Validate(new CreateGoodsReceiptRequest(Guid.NewGuid(), Items: [Line(quantity: 0)])).IsValid);
    }

    [Fact]
    public void List_requests_validate_status_and_dates()
    {
        var receipts = new GoodsReceiptListRequestValidator();

        Assert.True(receipts.Validate(new GoodsReceiptListRequest { Status = "confirmed" }).IsValid);
        Assert.False(receipts.Validate(new GoodsReceiptListRequest { Status = "REVERSED" }).IsValid);
        Assert.False(receipts.Validate(new GoodsReceiptListRequest { FromDate = Today, ToDate = Today.AddDays(-1) }).IsValid);
        Assert.True(receipts.Validate(new GoodsReceiptListRequest { FromDate = Today, ToDate = Today }).IsValid);
        Assert.False(receipts.Validate(new GoodsReceiptListRequest { PageSize = 0 }).IsValid);

        var movements = new StockMovementListRequestValidator();
        Assert.True(movements.Validate(new StockMovementListRequest { Type = "stock_in" }).IsValid);
        Assert.False(movements.Validate(new StockMovementListRequest { Type = "TRANSFER" }).IsValid);

        var lots = new InventoryLotListRequestValidator();
        Assert.True(lots.Validate(new InventoryLotListRequest { Status = "DEPLETED" }).IsValid);
        Assert.False(lots.Validate(new InventoryLotListRequest { Status = "LOST" }).IsValid);
    }

    [Theory]
    [InlineData("ACTIVE", true)]
    [InlineData("quarantined", true)]
    [InlineData("BLOCKED", true)]
    [InlineData("EXPIRED", true)]
    [InlineData("DEPLETED", false)]
    [InlineData("LOST", false)]
    [InlineData("", false)]
    public void Operators_may_set_every_lot_status_except_depleted(string status, bool valid) =>
        Assert.Equal(valid, new ChangeLotStatusRequestValidator().Validate(new ChangeLotStatusRequest(status)).IsValid);

    [Fact]
    public void Supplier_request_is_validated()
    {
        var validator = new SupplierRequestValidator();

        Assert.True(validator.Validate(new SupplierRequest("Supplier A", Email: "a@b.vn")).IsValid);
        Assert.False(validator.Validate(new SupplierRequest("")).IsValid);
        Assert.False(validator.Validate(new SupplierRequest("A", Email: "not-an-email")).IsValid);
        Assert.False(validator.Validate(new SupplierRequest("A", Code: new string('c', 51))).IsValid);
        Assert.False(validator.Validate(new SupplierRequest(new string('n', 256))).IsValid);
    }

    [Fact]
    public void Creating_a_product_that_requires_expiry_without_lot_tracking_is_a_validation_error()
    {
        var validator = new AgriSage.Application.Features.Products.Validators.CreateProductRequestValidator();
        var request = new AgriSage.Application.Features.Products.Dtos.Requests.CreateProductRequest(
            "SKU-1", "Product", Guid.NewGuid(),
            [new AgriSage.Application.Features.Products.Dtos.Requests.PackagingRequest(Guid.NewGuid(), 1, true, true, true)]);

        Assert.True(validator.Validate(request).IsValid);
        Assert.False(validator.Validate(request with { RequiresLotTracking = false, RequiresExpiryDate = true }).IsValid);
        Assert.True(validator.Validate(request with { RequiresLotTracking = false, RequiresExpiryDate = false }).IsValid);
    }
}
