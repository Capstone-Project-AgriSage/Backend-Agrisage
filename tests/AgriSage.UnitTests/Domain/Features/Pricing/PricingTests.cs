using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Pricing.Entities;
using AgriSage.Domain.Features.Pricing.Enums;
using static AgriSage.UnitTests.Domain.Features.Products.CatalogTestData;

namespace AgriSage.UnitTests.Domain.Features.Pricing;

public class PricingTests
{
    private static readonly DateTimeOffset EffectiveFrom = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static PriceList CreatePriceList() =>
        new(Guid.NewGuid(), "RETAIL-2026", "Retail 2026", EffectiveFrom);

    [Fact]
    public void New_price_list_starts_as_draft()
    {
        Assert.Equal(PriceListStatus.Draft, CreatePriceList().Status);
    }

    [Fact]
    public void Price_list_validity_end_must_be_after_start()
    {
        Assert.Throws<DomainException>(() =>
            new PriceList(Guid.NewGuid(), "P1", "Price 1", EffectiveFrom, effectiveTo: EffectiveFrom));

        var priceList = CreatePriceList();
        Assert.Throws<DomainException>(() => priceList.ChangeValidity(EffectiveFrom, EffectiveFrom.AddDays(-1)));
        Assert.Null(priceList.EffectiveTo);
    }

    [Fact]
    public void Price_list_lifecycle_draft_active_inactive_active()
    {
        var priceList = CreatePriceList();

        Assert.Throws<DomainException>(priceList.Deactivate);

        priceList.Activate();
        Assert.Equal(PriceListStatus.Active, priceList.Status);
        Assert.Throws<DomainException>(priceList.Activate);

        priceList.Deactivate();
        Assert.Equal(PriceListStatus.Inactive, priceList.Status);

        priceList.Activate();
        Assert.Equal(PriceListStatus.Active, priceList.Status);
    }

    [Fact]
    public void Price_list_item_rejects_negative_price()
    {
        var (product, _, box) = CreateProductWithPackagings();
        var storeProduct = CreateStoreProduct(product);

        Assert.Throws<DomainException>(() => new PriceListItem(Guid.NewGuid(), storeProduct, box, -1m));

        var item = new PriceListItem(Guid.NewGuid(), storeProduct, box, 0m);
        Assert.Throws<DomainException>(() => item.ChangeSellingPrice(-0.01m));
        Assert.Equal(0m, item.SellingPrice);
    }

    [Fact]
    public void Price_list_item_packaging_must_belong_to_store_product()
    {
        var (product, _, _) = CreateProductWithPackagings("SKU-A");
        var (_, _, otherBox) = CreateProductWithPackagings("SKU-B");

        Assert.Throws<DomainException>(() =>
            new PriceListItem(Guid.NewGuid(), CreateStoreProduct(product), otherBox, 100m));
    }

    [Fact]
    public void Customer_group_price_list_period_rules()
    {
        Assert.Throws<DomainException>(() =>
            new CustomerGroupPriceList(Guid.NewGuid(), Guid.NewGuid(), EffectiveFrom, Guid.NewGuid(), EffectiveFrom.AddDays(-1)));

        var mapping = new CustomerGroupPriceList(Guid.NewGuid(), Guid.NewGuid(), EffectiveFrom, Guid.NewGuid());
        Assert.True(mapping.IsCurrent);

        mapping.End(EffectiveFrom.AddMonths(1));
        Assert.False(mapping.IsCurrent);
        Assert.Throws<DomainException>(() => mapping.End(EffectiveFrom.AddMonths(2)));
    }
}
