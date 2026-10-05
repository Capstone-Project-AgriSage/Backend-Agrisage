using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Carts;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Pricing;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.OnlineOrders;

// FLOW_2 §4 (task F2.2) against PostgreSQL; every session rolls back.
[Collection(RealDb.WalkInPriceListCollection)]
public class CartDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CartService Carts(OnlineOrderTestData data) => new(data.Context, new CurrentFarmer(data.Context, data.User),
        new PriceResolver(data.Context), new RowLockService(data.Context), new NpgsqlErrorClassifier(), data.User,
        new DateTimeProvider());

    [RealDbFact]
    public async Task Adding_the_same_pair_twice_adds_to_one_line_and_prices_come_from_the_list()
    {
        await using var session = await RealDb.Session.StartAsync();
        var data = await OnlineOrderTestData.PrepareAsync(session);
        var product = await data.NewProductAsync();
        await data.ActAsNewFarmerAsync();
        var carts = Carts(data);

        Assert.Null((await carts.GetAsync(Token)).Id); // no cart yet: empty response, nothing created

        await carts.AddItemAsync(new(product.StoreProductId, product.BoxId, 2), Token);
        var cart = await carts.AddItemAsync(new(product.StoreProductId, product.BoxId, 1), Token);
        cart = await carts.AddItemAsync(new(product.StoreProductId, product.BottleId, 4), Token);

        Assert.NotNull(cart.Id);
        Assert.Equal(data.WalkInListId, cart.PriceListId);
        Assert.Equal(2, cart.Items.Count);
        var box = cart.Items.Single(i => i.ProductPackagingId == product.BoxId);
        Assert.Equal(3, box.Quantity);
        Assert.Equal(55_000m, box.UnitPrice);
        Assert.Equal(165_000m, box.LineTotalAmount);
        Assert.Equal(product.Sku, box.Sku);
        Assert.True(box.IsAvailable);
        Assert.Equal(165_000m + 40_000m, cart.SubtotalAmount);
    }

    [RealDbFact]
    public async Task Prices_are_recalculated_on_every_read()
    {
        await using var session = await RealDb.Session.StartAsync();
        var data = await OnlineOrderTestData.PrepareAsync(session);
        var product = await data.NewProductAsync();
        var farmer = await data.ActAsNewFarmerAsync();
        var carts = Carts(data);
        await carts.AddItemAsync(new(product.StoreProductId, product.BottleId, 2), Token);

        data.ActAsStaff();
        await data.PriceLists.UpsertItemsAsync(data.WalkInListId,
            new UpsertPriceListItemsRequest([new PriceListItemInput(product.StoreProductId, product.BottleId, 12_500m)]), Token);
        data.ActAs(farmer.UserId, "FARMER");

        var cart = await carts.GetAsync(Token);
        Assert.Equal(12_500m, Assert.Single(cart.Items).UnitPrice);
        Assert.Equal(25_000m, cart.SubtotalAmount);
    }

    [RealDbFact]
    public async Task Only_sellable_sale_packagings_can_be_added()
    {
        await using var session = await RealDb.Session.StartAsync();
        var data = await OnlineOrderTestData.PrepareAsync(session);
        var product = await data.NewProductAsync();
        await data.ActAsNewFarmerAsync();
        var carts = Carts(data);

        await Assert.ThrowsAsync<BusinessRuleException>(() => carts.AddItemAsync(new(product.StoreProductId, product.CartonId, 1), Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => carts.AddItemAsync(new(Guid.NewGuid(), product.BoxId, 1), Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => carts.AddItemAsync(
            new(product.StoreProductId, product.BoxId, OrderItemRequestValidator.MaxQuantity + 1), Token));
        Assert.Null((await carts.GetAsync(Token)).Id); // nothing was saved
    }

    [RealDbFact]
    public async Task Lines_that_can_no_longer_be_ordered_are_flagged_not_hidden()
    {
        await using var session = await RealDb.Session.StartAsync();
        var data = await OnlineOrderTestData.PrepareAsync(session);
        var sold = await data.NewProductAsync();
        var withdrawn = await data.NewProductAsync();
        var farmer = await data.ActAsNewFarmerAsync();
        var carts = Carts(data);
        await carts.AddItemAsync(new(sold.StoreProductId, sold.BoxId, 1), Token);
        await carts.AddItemAsync(new(withdrawn.StoreProductId, withdrawn.BoxId, 1), Token);

        data.ActAsStaff();
        await data.StoreProducts.SetSellableAsync(withdrawn.StoreProductId, false, Token);
        data.ActAs(farmer.UserId, "FARMER");

        var cart = await carts.GetAsync(Token);
        var line = cart.Items.Single(i => i.StoreProductId == withdrawn.StoreProductId);
        Assert.False(line.IsAvailable);
        Assert.Equal(CartUnavailableReason.NotSellable, line.UnavailableReason);
        Assert.Null(line.UnitPrice);
        Assert.Equal(55_000m, cart.SubtotalAmount); // only the available line counts

        // No price list applies any more: the cart still answers, every line NO_PRICE.
        data.ActAsStaff();
        await data.PriceLists.DeactivateAsync(data.WalkInListId, Token);
        data.ActAs(farmer.UserId, "FARMER");
        cart = await carts.GetAsync(Token);
        Assert.Null(cart.PriceListId);
        Assert.Equal(CartUnavailableReason.NoPrice, cart.Items.Single(i => i.StoreProductId == sold.StoreProductId).UnavailableReason);
        Assert.Equal(0m, cart.SubtotalAmount);
    }

    [RealDbFact]
    public async Task Update_remove_and_clear_work_only_on_the_farmers_own_active_cart()
    {
        await using var session = await RealDb.Session.StartAsync();
        var data = await OnlineOrderTestData.PrepareAsync(session);
        var product = await data.NewProductAsync();
        var owner = await data.ActAsNewFarmerAsync();
        var carts = Carts(data);
        var cart = await carts.AddItemAsync(new(product.StoreProductId, product.BoxId, 1), Token);
        cart = await carts.AddItemAsync(new(product.StoreProductId, product.BottleId, 1), Token);
        var box = cart.Items.Single(i => i.ProductPackagingId == product.BoxId);
        var bottle = cart.Items.Single(i => i.ProductPackagingId == product.BottleId);

        Assert.Equal(5, (await carts.UpdateItemAsync(box.Id, new(5), Token)).Items.Single(i => i.Id == box.Id).Quantity);
        var afterRemove = await carts.RemoveItemAsync(bottle.Id, Token);
        Assert.Equal(box.Id, Assert.Single(afterRemove.Items).Id);
        Assert.NotNull(await data.Context.CartItems.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.Id == bottle.Id).Select(i => i.DeletedAt).SingleAsync(Token));
        await Assert.ThrowsAsync<NotFoundException>(() => carts.UpdateItemAsync(bottle.Id, new(1), Token));

        await data.ActAsNewFarmerAsync(); // someone else
        await Assert.ThrowsAsync<NotFoundException>(() => carts.UpdateItemAsync(box.Id, new(2), Token));
        await Assert.ThrowsAsync<NotFoundException>(() => carts.RemoveItemAsync(box.Id, Token));

        data.ActAs(owner.UserId, "FARMER");
        await carts.ClearAsync(Token);
        Assert.Equal(CartStatus.Abandoned, (await data.Context.Carts.AsNoTracking().SingleAsync(c => c.Id == cart.Id, Token)).Status);
        Assert.Null((await carts.GetAsync(Token)).Id);
        await Assert.ThrowsAsync<NotFoundException>(() => carts.RemoveItemAsync(box.Id, Token));

        var next = await carts.AddItemAsync(new(product.StoreProductId, product.BoxId, 1), Token);
        Assert.NotEqual(cart.Id, next.Id); // a new ACTIVE cart
        await carts.ClearAsync(Token);
        await carts.ClearAsync(Token); // nothing left to clear: still fine
    }

    [RealDbFact]
    public async Task A_staff_account_without_a_farmer_profile_has_no_cart()
    {
        await using var session = await RealDb.Session.StartAsync();
        var data = await OnlineOrderTestData.PrepareAsync(session);
        await Assert.ThrowsAsync<ForbiddenException>(() => Carts(data).GetAsync(Token));
    }
}
