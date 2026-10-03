using AgriSage.Application.Features.Pricing;

namespace AgriSage.UnitTests.Application.Pricing;

// Request validation of the price list API (FLOW_1 §3), before any database access.
public class PriceListValidatorTests
{
    private static readonly DateTimeOffset From = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static PriceListItemInput Line(decimal price = 250_000m) => new(Guid.NewGuid(), Guid.NewGuid(), price);

    [Fact]
    public void A_price_list_needs_a_code_a_name_and_an_end_after_its_start()
    {
        var validator = new PriceListRequestValidator();

        Assert.True(validator.Validate(new PriceListRequest("RETAIL", "Giá lẻ", From)).IsValid);
        Assert.True(validator.Validate(new PriceListRequest("RETAIL", "Giá lẻ", From, From.AddMonths(6))).IsValid);

        var errors = validator.Validate(new PriceListRequest("", " ", From, From)).Errors.Select(e => e.PropertyName).ToList();
        Assert.Contains("Code", errors);
        Assert.Contains("Name", errors);
        Assert.Contains("EffectiveTo", errors);
        Assert.False(validator.Validate(new PriceListRequest(new string('C', 51), "Giá", From)).IsValid);
    }

    [Fact]
    public void Item_upserts_are_bounded_unique_per_pair_and_priced_as_money()
    {
        var validator = new UpsertPriceListItemsRequestValidator();
        var line = Line();

        Assert.True(validator.Validate(new UpsertPriceListItemsRequest([Line(), Line(0m), Line(12.5m)])).IsValid);
        Assert.False(validator.Validate(new UpsertPriceListItemsRequest([])).IsValid);
        Assert.False(validator.Validate(new UpsertPriceListItemsRequest([line, line with { SellingPrice = 1m }])).IsValid);
        Assert.False(validator.Validate(new UpsertPriceListItemsRequest([Line(1.234m)])).IsValid);
        Assert.False(validator.Validate(new UpsertPriceListItemsRequest([Line(-1m)])).IsValid);
        Assert.False(validator.Validate(new UpsertPriceListItemsRequest([line with { StoreProductId = Guid.Empty }])).IsValid);

        var tooMany = Enumerable.Range(0, UpsertPriceListItemsRequestValidator.MaxItems + 1).Select(_ => Line()).ToList();
        Assert.False(validator.Validate(new UpsertPriceListItemsRequest(tooMany)).IsValid);
        Assert.True(validator.Validate(new UpsertPriceListItemsRequest(tooMany.Take(UpsertPriceListItemsRequestValidator.MaxItems).ToList())).IsValid);
    }

    [Fact]
    public void List_filters_accept_only_known_statuses()
    {
        var validator = new PriceListListRequestValidator();

        Assert.True(validator.Validate(new PriceListListRequest { Status = "active" }).IsValid);
        Assert.False(validator.Validate(new PriceListListRequest { Status = "EXPIRED" }).IsValid);
        Assert.False(validator.Validate(new PriceListListRequest { PageSize = 0 }).IsValid);
    }
}
