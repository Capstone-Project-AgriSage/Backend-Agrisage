using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Tests.TestDoubles;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Pricing;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Services;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Pricing.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Products.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Orders;

// REAL PostgreSQL (opt-in AGRISAGE_DB_TESTS=1): order confirmation, FEFO reservation, preparing / ready and the
// reservation reads (F1.4), always rolled back. Active walk-in lists already in agrisage-dev are switched off inside the
// transaction first. The lot row locks run for real inside the test transaction; two staff confirming orders that share
// a lot at the same moment cannot be reproduced here because the test data is never committed.
[Collection(RealDb.WalkInPriceListCollection)]
public class OrderConfirmationDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Tag() => Guid.NewGuid().ToString("N")[..10];

    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    private static DateOnly Today => BusinessCalendar.Today(Now);

    private sealed class Env : IAsyncDisposable
    {
        public Env(AgriSageDbContext context, RealDb.MutableUser user)
        {
            Context = context;
            var errors = new NpgsqlErrorClassifier();
            var clock = new DateTimeProvider();
            var audit = new AuditTrail(context, user, clock);
            Categories = new CategoryService(context, errors);
            Products = new ProductService(context, errors);
            StoreProducts = new StoreProductService(context, errors);
            PriceLists = new PriceListService(context, clock, errors, audit);
            Orders = new OrderService(
                context, new OrderBuilder(context, new PriceResolver(context), user, clock, audit, new AgriSage.Application.Features.Credit.CreditEligibilityService(context, clock, Microsoft.Extensions.Options.Options.Create(new AgriSage.Application.Features.Credit.CreditPolicy()))), new OrderQueries(context),
                user, clock, errors, audit);
            Confirmer = new OrderConfirmer(context, new RowLockService(context), new StubOrderSettlementGuard(), user, clock, audit);
            Confirmation = new OrderConfirmationService(
                context, new RowLockService(context), Confirmer, new OrderQueries(context), clock, errors, audit);
        }

        public AgriSageDbContext Context { get; }

        public CategoryService Categories { get; }

        public ProductService Products { get; }

        public StoreProductService StoreProducts { get; }

        public PriceListService PriceLists { get; }

        public OrderService Orders { get; }

        public OrderConfirmer Confirmer { get; }

        public OrderConfirmationService Confirmation { get; }

        public User Actor { get; set; } = null!;

        public Guid StoreId { get; set; }

        public Guid Bottle { get; set; }

        public Guid WalkInListId { get; set; }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed record Sku(Guid StoreProductId, Guid BottleId, string Code);

    private static async Task<Guid> RoleIdAsync(AgriSageDbContext context, RoleCode code)
    {
        var role = await context.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Code == code, Token);
        if (role is null)
        {
            role = new Role(code, code.ToString());
            context.Roles.Add(role);
            await context.SaveChangesAsync(Token);
        }

        return role.Id;
    }

    private static async Task<Env> PrepareAsync(RealDb.Session session)
    {
        var user = new RealDb.MutableUser { Role = "SALES_STAFF" };
        var env = new Env(session.NewContext(), user);
        var context = env.Context;

        var unit = await context.Units.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Code == "BOTTLE", Token);
        if (unit is null)
        {
            unit = new Unit("BOTTLE", "Bottle");
            context.Units.Add(unit);
            await context.SaveChangesAsync(Token);
        }

        env.Bottle = unit.Id;
        env.Actor = new User(await RoleIdAsync(context, RoleCode.SalesStaff), "Confirm Tester", "hash", $"{Tag()}@example.test", null);
        context.Users.Add(env.Actor);
        user.UserId = env.Actor.Id;

        var storeId = await context.Stores.Where(s => s.Status == StoreStatus.Active).Select(s => s.Id).FirstOrDefaultAsync(Token);
        if (storeId == Guid.Empty)
        {
            var store = new Store($"T{Tag()}", "Test store", "1 Test Street", "Test");
            context.Stores.Add(store);
            storeId = store.Id;
        }

        env.StoreId = storeId;

        foreach (var list in await context.PriceLists.Where(p => p.StoreId == storeId && p.Status == PriceListStatus.Active)
                     .ToListAsync(Token))
        {
            list.Deactivate();
        }

        await context.SaveChangesAsync(Token);

        var walkIn = await env.PriceLists.CreateAsync(new PriceListRequest($"PL-{Tag()}", "Bảng giá thử", Now.AddDays(-1), null, true), Token);
        await env.PriceLists.ActivateAsync(walkIn.Id, Token);
        env.WalkInListId = walkIn.Id;

        return env;
    }

    // A bottle product (1 base unit per bottle) priced 10 000 in the walk-in list.
    private static async Task<Sku> NewSkuAsync(Env env)
    {
        var tag = Tag();
        var category = await env.Categories.CreateAsync(new CreateCategoryRequest($"C-{tag}", $"Category {tag}", null, 0, null), Token);
        var product = await env.Products.CreateAsync(
            new CreateProductRequest($"SKU-{tag}", $"Product {tag}", category.Id, [new PackagingRequest(env.Bottle, 1, true, false, true, "Bottle")]),
            Token);
        var storeProduct = await env.StoreProducts.CreateAsync(new CreateStoreProductRequest(product.Id), Token);
        var bottleId = product.Packagings.Single().Id;
        await env.PriceLists.UpsertItemsAsync(
            env.WalkInListId, new UpsertPriceListItemsRequest([new PriceListItemInput(storeProduct.Id, bottleId, 10_000m)]), Token);

        return new Sku(storeProduct.Id, bottleId, product.Sku);
    }

    private static async Task<InventoryLot> NewLotAsync(
        Env env, Sku sku, string number, long onHand, DateOnly? expiry, InventoryLotStatus status = InventoryLotStatus.Active)
    {
        var lot = new InventoryLot(sku.StoreProductId, number, null, expiry);
        lot.ReceiveStock(onHand, 5_000m);
        lot.ChangeStatus(status);
        env.Context.InventoryLots.Add(lot);
        await env.Context.SaveChangesAsync(Token);

        return lot;
    }

    private static Task<OrderResponse> NewOrderAsync(Env env, params (Sku Sku, long Quantity)[] lines) =>
        env.Orders.CreateAsync(
            new CreateCounterOrderRequest(
                "WALK_IN", "FULL_PAYMENT", "PICKUP",
                lines.Select(l => new OrderItemRequest(l.Sku.StoreProductId, l.Sku.BottleId, l.Quantity)).ToList()),
            Token);

    private static async Task<(long OnHand, long Reserved)> BalanceAsync(Env env, Guid lotId)
    {
        var balance = await env.Context.InventoryLotBalances.AsNoTracking().SingleAsync(b => b.InventoryLotId == lotId, Token);

        return (balance.QuantityOnHand, balance.QuantityReserved);
    }

    // ----- Confirming -----

    [RealDbFact]
    public async Task Confirming_reserves_the_earliest_expiring_lots_first_and_confirms_the_order()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var late = await NewLotAsync(env, sku, "L-LATE", 80, Today.AddDays(90));
        var early = await NewLotAsync(env, sku, "L-EARLY", 100, Today.AddDays(30));
        var order = await NewOrderAsync(env, (sku, 125));

        var confirmed = await env.Confirmation.ConfirmAsync(order.Id, Token);

        Assert.Equal(("CONFIRMED", env.Actor.Id), (confirmed.Status, confirmed.ConfirmedBy));
        Assert.NotNull(confirmed.ConfirmedAt);
        Assert.Equal((100L, 100L), await BalanceAsync(env, early.Id));
        Assert.Equal((80L, 25L), await BalanceAsync(env, late.Id));

        var reservation = await env.Confirmation.GetReservationAsync(order.Id, Token);
        Assert.Equal(("ACTIVE", env.Actor.Id), (reservation.Status, reservation.ReservedBy));
        Assert.Equal(["L-EARLY", "L-LATE"], reservation.Items.Select(i => i.LotNumber).Order());
        var earlyLine = reservation.Items.Single(i => i.InventoryLotId == early.Id);
        Assert.Equal((order.Items.Single().Id, 100L, 0L, 0L, 100L, Today.AddDays(30)),
            (earlyLine.OrderItemId, earlyLine.ReservedBaseQuantity, earlyLine.ConsumedBaseQuantity, earlyLine.ReleasedBaseQuantity, earlyLine.RemainingBaseQuantity, earlyLine.ExpiryDate));
        Assert.Equal(25L, reservation.Items.Single(i => i.InventoryLotId == late.Id).ReservedBaseQuantity);

        var audit = await env.Context.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityId == order.Id && a.Action == "ORDER_CONFIRMED", Token);
        Assert.Equal(env.Actor.Id, audit.ActorUserId);

        // A confirmed order's "suggestions" are the open lines of its reservation.
        var held = await env.Confirmation.GetFefoSuggestionsAsync(order.Id, Token);
        var line = Assert.Single(held.Items);
        Assert.Equal((125L, 0L), (line.RemainingBaseQuantity, line.ShortageBaseQuantity));
        Assert.Equal([100L, 25L], line.Lots.Select(l => l.SuggestedBaseQuantity));
    }

    [RealDbFact]
    public async Task Expired_blocked_quarantined_and_empty_lots_are_not_reserved_and_undated_lots_come_last()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var expired = await NewLotAsync(env, sku, "L-EXPIRED", 500, Today.AddDays(-1));
        var blocked = await NewLotAsync(env, sku, "L-BLOCKED", 500, Today.AddDays(5), InventoryLotStatus.Blocked);
        var quarantined = await NewLotAsync(env, sku, "L-QUAR", 500, Today.AddDays(6), InventoryLotStatus.Quarantined);
        var undated = await NewLotAsync(env, sku, "L-UNDATED", 500, null);
        var today = await NewLotAsync(env, sku, "L-TODAY", 5, Today);
        var order = await NewOrderAsync(env, (sku, 20));

        await env.Confirmation.ConfirmAsync(order.Id, Token);

        // The lot expiring today is still sellable and is first; the undated lot covers the rest.
        Assert.Equal((5L, 5L), await BalanceAsync(env, today.Id));
        Assert.Equal((500L, 15L), await BalanceAsync(env, undated.Id));
        foreach (var skipped in new[] { expired, blocked, quarantined })
        {
            Assert.Equal((500L, 0L), await BalanceAsync(env, skipped.Id));
        }
    }

    [RealDbFact]
    public async Task A_shortage_on_any_line_reserves_nothing_and_names_the_lines()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var plenty = await NewSkuAsync(env);
        var scarce = await NewSkuAsync(env);
        var plentyLot = await NewLotAsync(env, plenty, "L-P", 100, Today.AddDays(30));
        var scarceLot = await NewLotAsync(env, scarce, "L-S", 4, Today.AddDays(30));
        var order = await NewOrderAsync(env, (plenty, 10), (scarce, 10));

        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Confirmation.ConfirmAsync(order.Id, Token));

        Assert.Equal(["items[1]"], refused.Errors!.Keys);
        Assert.Contains("6 base units short", refused.Errors["items[1]"].Single());
        Assert.Equal((100L, 0L), await BalanceAsync(env, plentyLot.Id));
        Assert.Equal((4L, 0L), await BalanceAsync(env, scarceLot.Id));
        Assert.Equal("PENDING_CONFIRMATION", (await env.Orders.GetAsync(order.Id, Token)).Status);
        Assert.Empty(await env.Context.InventoryReservations.AsNoTracking().Where(r => r.OrderId == order.Id).ToListAsync(Token));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Confirmation.GetReservationAsync(order.Id, Token));
    }

    [RealDbFact]
    public async Task Stock_reserved_by_one_order_is_not_available_to_the_next()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var lot = await NewLotAsync(env, sku, "L1", 120, Today.AddDays(30));
        var first = await NewOrderAsync(env, (sku, 100));
        var second = await NewOrderAsync(env, (sku, 50));

        var suggestion = await env.Confirmation.GetFefoSuggestionsAsync(second.Id, Token);
        Assert.Equal((0L, 120L, 50L), (suggestion.Items.Single().ShortageBaseQuantity, suggestion.Items.Single().Lots.Single().AvailableBaseQuantity, suggestion.Items.Single().Lots.Single().SuggestedBaseQuantity));

        await env.Confirmation.ConfirmAsync(first.Id, Token);
        var after = await env.Confirmation.GetFefoSuggestionsAsync(second.Id, Token);
        Assert.Equal((30L, 20L), (after.Items.Single().ShortageBaseQuantity, after.Items.Single().Lots.Single().AvailableBaseQuantity));
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Confirmation.ConfirmAsync(second.Id, Token));
        Assert.Contains("30 base units short", refused.Errors!["items[0]"].Single());
        Assert.Equal((120L, 100L), await BalanceAsync(env, lot.Id));
    }

    [RealDbFact]
    public async Task Credit_orders_wait_for_the_real_settlement_guard_and_reserve_nothing()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var lot = await NewLotAsync(env, sku, "L1", 100, Today.AddDays(30));
        var farmerUser = new User(await RoleIdAsync(env.Context, RoleCode.Farmer), "Nông dân", "hash", $"{Tag()}@example.test", null);
        var farmer = new AgriSage.Domain.Features.Customers.Entities.FarmerProfile(farmerUser.Id);
        env.Context.AddRange(farmerUser, farmer);
        await env.Context.SaveChangesAsync(Token);
        var order = await env.Orders.CreateAsync(
            new CreateCounterOrderRequest(
                "REGISTERED", "CREDIT", "PICKUP", [new OrderItemRequest(sku.StoreProductId, sku.BottleId, 10)], farmer.Id),
            Token);

        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Confirmation.ConfirmAsync(order.Id, Token));

        Assert.Equal((100L, 0L), await BalanceAsync(env, lot.Id));
        Assert.Equal("PENDING_CONFIRMATION", (await env.Orders.GetAsync(order.Id, Token)).Status);
    }

    [RealDbFact]
    public async Task Only_a_pending_order_with_lines_can_be_confirmed_and_only_once()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var lot = await NewLotAsync(env, sku, "L1", 100, Today.AddDays(30));
        var order = await NewOrderAsync(env, (sku, 10));
        var empty = await env.Orders.RemoveItemAsync(order.Id, order.Items.Single().Id, Token);
        Assert.Empty(empty.Items);

        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Confirmation.ConfirmAsync(order.Id, Token));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Confirmation.ConfirmAsync(Guid.NewGuid(), Token));

        var again = await NewOrderAsync(env, (sku, 10));
        await env.Confirmation.ConfirmAsync(again.Id, Token);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Confirmation.ConfirmAsync(again.Id, Token));
        Assert.Equal((100L, 10L), await BalanceAsync(env, lot.Id));
        Assert.Single(await env.Context.InventoryReservations.AsNoTracking().Where(r => r.OrderId == again.Id).ToListAsync(Token));
    }

    [RealDbFact]
    public async Task A_line_switched_off_after_the_order_was_made_blocks_confirmation_but_not_a_confirmed_order()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var lot = await NewLotAsync(env, sku, "L1", 100, Today.AddDays(30));
        var order = await NewOrderAsync(env, (sku, 10));
        var storeProduct = await env.Context.StoreProducts.Include(sp => sp.Product).SingleAsync(sp => sp.Id == sku.StoreProductId, Token);
        var packaging = await env.Context.ProductPackagings.SingleAsync(p => p.Id == sku.BottleId, Token);

        async Task AssertRefusedAsync(string expected)
        {
            await env.Context.SaveChangesAsync(Token);
            var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Confirmation.ConfirmAsync(order.Id, Token));
            Assert.Contains(expected, refused.Errors!["items[0]"].Single());
            Assert.Contains("no longer be sold", refused.Message);
            Assert.Equal((100L, 0L), await BalanceAsync(env, lot.Id));
            Assert.Equal("PENDING_CONFIRMATION", (await env.Orders.GetAsync(order.Id, Token)).Status);
        }

        storeProduct.MarkNotSellable();
        await AssertRefusedAsync(sku.Code);
        storeProduct.MarkSellable();

        storeProduct.Deactivate();
        await AssertRefusedAsync("not for sale");
        storeProduct.Activate();

        storeProduct.Product.ChangeStatus(ProductStatus.Discontinued);
        await AssertRefusedAsync("not for sale");
        storeProduct.Product.ChangeStatus(ProductStatus.Active);

        packaging.ChangeStatus(PackagingStatus.Inactive);
        await AssertRefusedAsync("ACTIVE sale packagings");
        packaging.ChangeStatus(PackagingStatus.Active);

        // Switched back on, the same order confirms; its price is still the one taken when it was made.
        await env.Context.SaveChangesAsync(Token);
        var confirmed = await env.Confirmation.ConfirmAsync(order.Id, Token);
        Assert.Equal(("CONFIRMED", 100_000m), (confirmed.Status, confirmed.TotalAmount));

        // A confirmed order is a commitment: switching the product off now does not stop its pickup.
        storeProduct.MarkNotSellable();
        await env.Context.SaveChangesAsync(Token);
        Assert.Equal("READY_FOR_FULFILLMENT", (await env.Confirmation.MarkReadyAsync(order.Id, Token)).Status);
    }

    // ----- Preparing / ready -----

    [RealDbFact]
    public async Task Preparing_is_optional_and_ready_follows_confirmed_or_preparing()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        await NewLotAsync(env, sku, "L1", 100, Today.AddDays(30));
        var skipping = await NewOrderAsync(env, (sku, 5));
        var stepping = await NewOrderAsync(env, (sku, 5));

        await Assert.ThrowsAsync<DomainException>(() => env.Confirmation.StartPreparingAsync(skipping.Id, Token));
        await Assert.ThrowsAsync<DomainException>(() => env.Confirmation.MarkReadyAsync(skipping.Id, Token));

        await env.Confirmation.ConfirmAsync(skipping.Id, Token);
        Assert.Equal("READY_FOR_FULFILLMENT", (await env.Confirmation.MarkReadyAsync(skipping.Id, Token)).Status);
        await Assert.ThrowsAsync<DomainException>(() => env.Confirmation.StartPreparingAsync(skipping.Id, Token));

        await env.Confirmation.ConfirmAsync(stepping.Id, Token);
        Assert.Equal("PREPARING", (await env.Confirmation.StartPreparingAsync(stepping.Id, Token)).Status);
        await Assert.ThrowsAsync<DomainException>(() => env.Confirmation.StartPreparingAsync(stepping.Id, Token));
        Assert.Equal("READY_FOR_FULFILLMENT", (await env.Confirmation.MarkReadyAsync(stepping.Id, Token)).Status);
        await Assert.ThrowsAsync<NotFoundException>(() => env.Confirmation.MarkReadyAsync(Guid.NewGuid(), Token));
    }

    // ----- Suggestions -----

    [RealDbFact]
    public async Task Suggestions_follow_fefo_for_a_pending_order_and_are_refused_for_a_cancelled_one()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var early = await NewLotAsync(env, sku, "L-EARLY", 100, Today.AddDays(30));
        var late = await NewLotAsync(env, sku, "L-LATE", 80, Today.AddDays(90));
        await NewLotAsync(env, sku, "L-EXPIRED", 999, Today.AddDays(-5));
        var order = await NewOrderAsync(env, (sku, 125));

        var item = Assert.Single((await env.Confirmation.GetFefoSuggestionsAsync(order.Id, Token)).Items);

        Assert.Equal((order.Items.Single().Id, 125L, 125L, 0L), (item.OrderItemId, item.BaseQuantity, item.RemainingBaseQuantity, item.ShortageBaseQuantity));
        Assert.Equal([early.Id, late.Id], item.Lots.Select(l => l.InventoryLotId));
        Assert.Equal([(100L, 100L), (80L, 25L)], item.Lots.Select(l => (l.AvailableBaseQuantity, l.SuggestedBaseQuantity)));
        Assert.Equal(("L-EARLY", Today.AddDays(30)), (item.Lots[0].LotNumber, item.Lots[0].ExpiryDate));
        Assert.Equal((100L, 0L), await BalanceAsync(env, early.Id));

        var entity = await env.Context.Orders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id, Token);
        entity.Cancel(env.Actor.Id, Now, "Khách đổi ý");
        await env.Context.SaveChangesAsync(Token);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Confirmation.GetFefoSuggestionsAsync(order.Id, Token));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Confirmation.GetFefoSuggestionsAsync(Guid.NewGuid(), Token));
    }

    // ----- The shared core with explicit lots (quick counter sale) -----

    [RealDbFact]
    public async Task The_core_reserves_the_lots_the_staff_chose_and_refuses_picks_that_do_not_fit_the_line()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var other = await NewSkuAsync(env);
        var early = await NewLotAsync(env, sku, "L-EARLY", 100, Today.AddDays(30));
        var late = await NewLotAsync(env, sku, "L-LATE", 100, Today.AddDays(90));
        var foreign = await NewLotAsync(env, other, "L-OTHER", 100, Today.AddDays(30));
        var expired = await NewLotAsync(env, sku, "L-EXPIRED", 100, Today.AddDays(-1));
        var created = await NewOrderAsync(env, (sku, 60));
        var itemId = created.Items.Single().Id;
        var order = await env.Context.Orders.Include(o => o.Items).SingleAsync(o => o.Id == created.Id, Token);

        // The same lot named twice adds up like one pick: 30 + 30 of the late lot is 60.
        foreach (var bad in new LotPick[][]
                 {
                     [new(itemId, late.Id, 50)],
                     [new(itemId, late.Id, 70)],
                     [new(itemId, foreign.Id, 60)],
                     [new(itemId, expired.Id, 60)],
                     [new(itemId, Guid.NewGuid(), 60)]
                 })
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => env.Confirmer.ConfirmCoreAsync(order, bad, Token));
        }

        // Each refusal happened before anything was reserved or confirmed.
        Assert.Equal((100L, 0L), await BalanceAsync(env, late.Id));
        Assert.Equal(Domain.Features.Orders.Enums.OrderStatus.PendingConfirmation, order.Status);

        var reservation = await env.Confirmer.ConfirmCoreAsync(order, [new LotPick(itemId, late.Id, 30), new LotPick(itemId, late.Id, 30)], Token);
        await env.Context.SaveChangesAsync(Token);

        // The staff's lot is used although FEFO would have taken the earlier one.
        Assert.Equal((100L, 60L), await BalanceAsync(env, late.Id));
        Assert.Equal((100L, 0L), await BalanceAsync(env, early.Id));
        Assert.Equal(("CONFIRMED", late.Id), ((await env.Orders.GetAsync(created.Id, Token)).Status, Assert.Single(reservation.Items).InventoryLotId));
    }
}
