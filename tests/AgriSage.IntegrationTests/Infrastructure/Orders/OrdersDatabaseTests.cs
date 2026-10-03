using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Pricing;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Dtos.Responses;
using AgriSage.Application.Features.Products.Services;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Pricing.Entities;
using AgriSage.Domain.Features.Pricing.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Orders;

// REAL PostgreSQL (opt-in AGRISAGE_DB_TESTS=1): staff counter orders and the OrderBuilder (F1.2), always rolled back.
// Active walk-in lists and default groups already in agrisage-dev are switched off inside the transaction first.
// Two people editing one order at once cannot be reproduced here (the test data is never committed); the version
// check itself is covered by the shared concurrency tests.
[Collection(RealDb.WalkInPriceListCollection)]
public class OrdersDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Tag() => Guid.NewGuid().ToString("N")[..10];

    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

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
            Builder = new OrderBuilder(context, new PriceResolver(context), user, clock, audit);
            Orders = new OrderService(context, Builder, new OrderQueries(context), user, clock, errors, audit);
        }

        public AgriSageDbContext Context { get; }

        public CategoryService Categories { get; }

        public ProductService Products { get; }

        public StoreProductService StoreProducts { get; }

        public PriceListService PriceLists { get; }

        public OrderBuilder Builder { get; }

        public OrderService Orders { get; }

        public User Actor { get; set; } = null!;

        public Guid StoreId { get; set; }

        public Guid Bottle { get; set; }

        public Guid Box { get; set; }

        public Guid Carton { get; set; }

        public Guid WalkInListId { get; set; }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed record Priced(ProductResponse Product, Guid StoreProductId, Guid BottleId, Guid BoxId, Guid CartonId);

    // A walk-in default list is created and activated, so a counter order can be priced from the start.
    private static async Task<Env> PrepareAsync(RealDb.Session session)
    {
        var user = new RealDb.MutableUser { Role = "SALES_STAFF" };
        var env = new Env(session.NewContext(), user);
        var context = env.Context;

        async Task<Guid> UnitAsync(string code, string name)
        {
            var unit = await context.Units.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Code == code, Token);
            if (unit is null)
            {
                unit = new Unit(code, name);
                context.Units.Add(unit);
                await context.SaveChangesAsync(Token);
            }

            return unit.Id;
        }

        env.Bottle = await UnitAsync("BOTTLE", "Bottle");
        env.Box = await UnitAsync("BOX", "Box");
        env.Carton = await UnitAsync("CARTON", "Carton");

        env.Actor = new User(await RoleIdAsync(context, RoleCode.SalesStaff), "Orders Tester", "hash", $"{Tag()}@example.test", null);
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

        foreach (var group in await context.CustomerGroups.Where(g => g.StoreId == storeId && g.IsDefault).ToListAsync(Token))
        {
            group.UnsetDefault();
        }

        await context.SaveChangesAsync(Token);

        env.WalkInListId = (await NewListAsync(env, walkIn: true)).Id;
        return env;
    }

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

    // Bottle = base + sale (10 000), Box of 6 = purchase + sale (55 000), Carton of 24 = purchase only. The walk-in list
    // prices the bottle and the box; the carton has no price and is not sold.
    private static async Task<Priced> NewProductAsync(Env env, Guid? priceListId = null, decimal bottle = 10_000m, decimal box = 55_000m)
    {
        var tag = Tag();
        var category = await env.Categories.CreateAsync(new CreateCategoryRequest($"C-{tag}", $"Category {tag}", null, 0, null), Token);
        var product = await env.Products.CreateAsync(
            new CreateProductRequest(
                $"SKU-{tag}", $"Product {tag}", category.Id,
                [
                    new PackagingRequest(env.Bottle, 1, true, false, true, "Bottle"),
                    new PackagingRequest(env.Box, 6, false, true, true, null),
                    new PackagingRequest(env.Carton, 24, false, true, false, "Carton of 24")
                ]),
            Token);
        var storeProduct = await env.StoreProducts.CreateAsync(new CreateStoreProductRequest(product.Id), Token);

        Guid Packaging(Guid unit) => product.Packagings.Single(p => p.UnitId == unit).Id;
        var priced = new Priced(product, storeProduct.Id, Packaging(env.Bottle), Packaging(env.Box), Packaging(env.Carton));
        await env.PriceLists.UpsertItemsAsync(
            priceListId ?? env.WalkInListId,
            new UpsertPriceListItemsRequest(
            [
                new PriceListItemInput(priced.StoreProductId, priced.BottleId, bottle),
                new PriceListItemInput(priced.StoreProductId, priced.BoxId, box)
            ]),
            Token);

        return priced;
    }

    private static async Task<PriceListResponse> NewListAsync(Env env, bool walkIn = false)
    {
        var list = await env.PriceLists.CreateAsync(new PriceListRequest($"PL-{Tag()}", "Bảng giá thử", Now.AddDays(-1), null, walkIn), Token);

        return await env.PriceLists.ActivateAsync(list.Id, Token);
    }

    private sealed record Farmer(Guid ProfileId, Guid UserId, string Name, string Phone);

    private static async Task<Farmer> NewFarmerAsync(Env env)
    {
        var phone = "09" + Random.Shared.Next(10_000_000, 99_999_999);
        var name = $"Nông dân {Tag()}";
        var user = new User(await RoleIdAsync(env.Context, RoleCode.Farmer), name, "hash", $"{Tag()}@example.test", phone);
        var farmer = new FarmerProfile(user.Id);
        env.Context.AddRange(user, farmer);
        await env.Context.SaveChangesAsync(Token);

        return new Farmer(farmer.Id, user.Id, name, phone);
    }

    private static CreateCounterOrderRequest Walk(
        IReadOnlyList<OrderItemRequest> items, string? name = null, string? phone = null, string fulfillment = "PICKUP",
        DeliveryAddressRequest? address = null, Guid? addressId = null, string? note = null) =>
        new("WALK_IN", "FULL_PAYMENT", fulfillment, items, null, name, phone, addressId, address, note);

    private static OrderItemRequest Line(Guid storeProductId, Guid packagingId, long quantity, decimal? price = null, string? reason = null) =>
        new(storeProductId, packagingId, quantity, price, reason);

    private static DeliveryAddressRequest Address() =>
        new(" Chú Tư ", "0912 345 678", " Ấp 3 ", "Cần Thơ", "Xã X", " ", null, null);

    // ----- Creating -----

    [RealDbFact]
    public async Task A_walk_in_order_is_named_khach_le_by_default_and_snapshots_prices_and_lines()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var priced = await NewProductAsync(env);

        var nameless = await env.Orders.CreateAsync(
            Walk([Line(priced.StoreProductId, priced.BottleId, 3), Line(priced.StoreProductId, priced.BoxId, 2)], name: "  ", phone: "0901 234 567"),
            Token);
        var named = await env.Orders.CreateAsync(Walk([Line(priced.StoreProductId, priced.BottleId, 1)], name: " Anh Ba "), Token);

        Assert.Equal(("Khách lẻ", "0901234567"), (nameless.CustomerName, nameless.CustomerPhone));
        Assert.Equal("Anh Ba", named.CustomerName);
        Assert.Equal(("COUNTER", "WALK_IN", "PENDING_CONFIRMATION", "FULL_PAYMENT", "PICKUP"),
            (nameless.Source, nameless.CustomerType, nameless.Status, nameless.SettlementType, nameless.FulfillmentType));
        Assert.Equal((env.WalkInListId, null), (nameless.PriceListId, nameless.CustomerGroupId));
        Assert.Null(nameless.DeliveryAddress);
        Assert.Equal(env.Actor.Id, nameless.CreatedBy);
        // 3 × 10 000 + 2 × 55 000
        Assert.Equal((140_000m, 140_000m), (nameless.SubtotalAmount, nameless.TotalAmount));
        var bottle = nameless.Items.Single(i => i.ProductPackagingId == priced.BottleId);
        var box = nameless.Items.Single(i => i.ProductPackagingId == priced.BoxId);
        Assert.Equal((priced.Product.Sku, priced.Product.Name, "Bottle", 3L, 1L, 3L), (bottle.Sku, bottle.ProductName, bottle.PackagingName, bottle.Quantity, bottle.ConversionToBase, bottle.BaseQuantity));
        // A packaging without a name takes the unit name.
        var boxUnitName = (await env.Context.Units.AsNoTracking().SingleAsync(u => u.Id == env.Box, Token)).Name;
        Assert.Equal((boxUnitName, 12L, 12L, 110_000m), (box.PackagingName, box.BaseQuantity, box.RemainingBaseQuantity, box.LineTotalAmount));
        Assert.All(nameless.Items, i => Assert.Equal((false, i.SuggestedUnitPrice, "PENDING"), (i.PriceOverridden, i.UnitPrice, i.Status)));

        // Numbers climb within the day: OD-yyyyMMdd-NNNN.
        var day = BusinessCalendar.Today(DateTimeOffset.UtcNow);
        Assert.Equal(DocumentNumbers.SequenceOf(nameless.OrderNumber, DocumentNumbers.Order, day) + 1,
            DocumentNumbers.SequenceOf(named.OrderNumber, DocumentNumbers.Order, day));
    }

    [RealDbFact]
    public async Task A_registered_order_takes_name_phone_and_the_group_price_list_from_the_farmer()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var farmer = await NewFarmerAsync(env);
        var group = new CustomerGroup(env.StoreId, $"G{Tag()}", "Khách quen");
        env.Context.CustomerGroups.Add(group);
        var groupList = await NewListAsync(env);
        env.Context.CustomerGroupPriceLists.Add(new CustomerGroupPriceList(group.Id, groupList.Id, Now.AddMinutes(-5), env.Actor.Id));
        env.Context.CustomerGroupAssignments.Add(new CustomerGroupAssignment(farmer.ProfileId, group.Id, Now.AddMinutes(-5), env.Actor.Id));
        await env.Context.SaveChangesAsync(Token);
        var priced = await NewProductAsync(env, groupList.Id, bottle: 9_000m);

        var order = await env.Orders.CreateAsync(
            new CreateCounterOrderRequest(
                "REGISTERED", "CREDIT", "PICKUP", [Line(priced.StoreProductId, priced.BottleId, 10)],
                farmer.ProfileId, "Client typed name", "0999999999"),
            Token);

        Assert.Equal((farmer.Name, farmer.Phone, farmer.ProfileId), (order.CustomerName, order.CustomerPhone, order.FarmerProfileId));
        Assert.Equal((group.Id, groupList.Id, "CREDIT", 90_000m), (order.CustomerGroupId, order.PriceListId, order.SettlementType, order.TotalAmount));
        Assert.Null(order.CreditTermDays);

        // The walk-in list has no price for this product; the order's list is the group's, so it was priced.
        await env.PriceLists.DeactivateAsync(groupList.Id, Token);
        var again = await env.Orders.GetAsync(order.Id, Token);
        Assert.Equal((groupList.Id, 90_000m), (again.PriceListId, again.TotalAmount));
    }

    [RealDbFact]
    public async Task A_walk_in_customer_cannot_use_credit_or_a_farmer_and_unknown_farmers_are_not_found()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var priced = await NewProductAsync(env);
        var items = new[] { Line(priced.StoreProductId, priced.BottleId, 1) };

        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Orders.CreateAsync(
            new CreateCounterOrderRequest("WALK_IN", "CREDIT", "PICKUP", items), Token));
        var farmer = await NewFarmerAsync(env);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Orders.CreateAsync(
            new CreateCounterOrderRequest("WALK_IN", "FULL_PAYMENT", "PICKUP", items, farmer.ProfileId), Token));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Orders.CreateAsync(
            new CreateCounterOrderRequest("REGISTERED", "FULL_PAYMENT", "PICKUP", items, Guid.NewGuid()), Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Orders.CreateAsync(Walk(items, phone: "12345"), Token));
        Assert.Equal(0, await env.Context.Orders.CountAsync(o => o.CreatedBy == env.Actor.Id, Token));
    }

    [RealDbFact]
    public async Task Lines_that_cannot_be_sold_or_priced_are_reported_one_by_one_and_nothing_is_saved()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var priced = await NewProductAsync(env);
        var other = await NewProductAsync(env);

        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Orders.CreateAsync(
            Walk(
            [
                Line(priced.StoreProductId, priced.BottleId, 1),
                Line(priced.StoreProductId, priced.CartonId, 1),
                Line(priced.StoreProductId, other.BoxId, 1),
                Line(Guid.NewGuid(), priced.BoxId, 1)
            ]),
            Token));

        Assert.Equal(["items[1]", "items[2]", "items[3]"], refused.Errors!.Keys.Order());
        Assert.Equal(0, await env.Context.Orders.CountAsync(o => o.CreatedBy == env.Actor.Id, Token));

        // A sale packaging missing from the price list has no price.
        await env.PriceLists.DeleteItemAsync(
            env.WalkInListId,
            (await env.PriceLists.ListItemsAsync(env.WalkInListId, new PriceListItemListRequest { Search = other.Product.Sku }, Token))
            .Items.Single(i => i.ProductPackagingId == other.BoxId).Id,
            Token);
        var noPrice = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Orders.CreateAsync(
            Walk([Line(other.StoreProductId, other.BoxId, 1)]), Token));
        Assert.Contains("no price", noPrice.Errors!["items[0]"].Single());
    }

    // ----- Price overrides -----

    [RealDbFact]
    public async Task A_price_override_keeps_the_suggested_price_actor_and_reason_and_is_audited()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var priced = await NewProductAsync(env);

        var created = await env.Orders.CreateAsync(
            Walk(
            [
                Line(priced.StoreProductId, priced.BottleId, 2, 8_000m, " Khách quen "),
                Line(priced.StoreProductId, priced.BoxId, 1, 55_000m, "ignored: same as suggested")
            ]),
            Token);
        var discounted = created.Items.Single(i => i.ProductPackagingId == priced.BottleId);
        var plain = created.Items.Single(i => i.ProductPackagingId == priced.BoxId);

        Assert.Equal((10_000m, 8_000m, true, "Khách quen", env.Actor.Id), (discounted.SuggestedUnitPrice, discounted.UnitPrice, discounted.PriceOverridden, discounted.OverrideReason, discounted.OverriddenBy));
        Assert.Equal((false, null), (plain.PriceOverridden, plain.OverrideReason));
        Assert.Equal(71_000m, created.TotalAmount);

        // Another price needs a reason.
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Orders.CreateAsync(
            Walk([Line(priced.StoreProductId, priced.BottleId, 1, 7_000m, "  ")]), Token));
        Assert.Contains("reason", refused.Errors!["items[0]"].Single());

        // PUT .../price sets another override, the suggested price itself restores, DELETE restores.
        var changed = await env.Orders.OverrideItemPriceAsync(created.Id, plain.Id, new OverrideOrderItemPriceRequest(50_000m, "Hàng cận date"), Token);
        Assert.Equal((50_000m, true, 66_000m), (changed.Items.Single(i => i.Id == plain.Id).UnitPrice, changed.Items.Single(i => i.Id == plain.Id).PriceOverridden, changed.TotalAmount));
        var back = await env.Orders.OverrideItemPriceAsync(created.Id, plain.Id, new OverrideOrderItemPriceRequest(55_000m, "Về giá niêm yết"), Token);
        Assert.False(back.Items.Single(i => i.Id == plain.Id).PriceOverridden);
        var restored = await env.Orders.RestoreItemPriceAsync(created.Id, discounted.Id, Token);
        Assert.Equal((10_000m, false, 75_000m), (restored.Items.Single(i => i.Id == discounted.Id).UnitPrice, restored.Items.Single(i => i.Id == discounted.Id).PriceOverridden, restored.TotalAmount));

        var audit = await env.Context.AuditLogs.AsNoTracking().Where(a => a.EntityId == created.Id).OrderBy(a => a.OccurredAt).ThenBy(a => a.Id).ToListAsync(Token);
        Assert.Equal(["PRICE_OVERRIDE", "PRICE_OVERRIDE", "PRICE_OVERRIDE_REMOVED", "PRICE_OVERRIDE_REMOVED"], audit.Select(a => a.Action));
        Assert.All(audit, a => Assert.Equal((env.Actor.Id, "ORDER"), (a.ActorUserId, a.EntityType)));
        Assert.Equal("Khách quen", audit[0].Reason);
        Assert.Contains("10000", audit[0].OldValues);
        Assert.Contains("8000", audit[0].NewValues);
    }

    // ----- Editing -----

    [RealDbFact]
    public async Task Lines_are_added_changed_and_removed_with_the_orders_own_prices_and_no_duplicates()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var priced = await NewProductAsync(env);
        var created = await env.Orders.CreateAsync(Walk([Line(priced.StoreProductId, priced.BottleId, 2)]), Token);
        var bottle = created.Items.Single();

        // The walk-in list changes after the order exists: the existing line keeps its snapshot and a new line is
        // priced by the order's own list.
        await env.PriceLists.UpsertItemsAsync(
            env.WalkInListId, new UpsertPriceListItemsRequest([new PriceListItemInput(priced.StoreProductId, priced.BottleId, 99_000m)]), Token);
        var added = await env.Orders.AddItemAsync(created.Id, Line(priced.StoreProductId, priced.BoxId, 1), Token);
        Assert.Equal(75_000m, added.TotalAmount);
        Assert.Equal(10_000m, added.Items.Single(i => i.Id == bottle.Id).UnitPrice);

        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Orders.AddItemAsync(created.Id, Line(priced.StoreProductId, priced.BottleId, 1), Token));

        var more = await env.Orders.ChangeItemQuantityAsync(created.Id, bottle.Id, new ChangeOrderItemQuantityRequest(5), Token);
        Assert.Equal((5L, 50_000m, 105_000m), (more.Items.Single(i => i.Id == bottle.Id).Quantity, more.Items.Single(i => i.Id == bottle.Id).LineTotalAmount, more.TotalAmount));

        var removed = await env.Orders.RemoveItemAsync(created.Id, bottle.Id, Token);
        Assert.Equal((1, 55_000m), (removed.Items.Count, removed.TotalAmount));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Orders.RemoveItemAsync(created.Id, bottle.Id, Token));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Orders.ChangeItemQuantityAsync(created.Id, Guid.NewGuid(), new ChangeOrderItemQuantityRequest(1), Token));
        Assert.Single((await env.Orders.GetAsync(created.Id, Token)).Items);

        var actions = await env.Context.AuditLogs.AsNoTracking().Where(a => a.EntityId == created.Id).Select(a => a.Action).ToListAsync(Token);
        Assert.Contains("ORDER_ITEM_QUANTITY_CHANGED", actions);
        Assert.Contains("ORDER_ITEM_REMOVED", actions);
    }

    [RealDbFact]
    public async Task Only_a_pending_order_can_be_changed()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var priced = await NewProductAsync(env);
        var created = await env.Orders.CreateAsync(Walk([Line(priced.StoreProductId, priced.BottleId, 2)]), Token);
        var itemId = created.Items.Single().Id;

        var order = await env.Context.Orders.Include(o => o.Items).SingleAsync(o => o.Id == created.Id, Token);
        order.Confirm(env.Actor.Id, Now);
        await env.Context.SaveChangesAsync(Token);

        await Assert.ThrowsAsync<DomainException>(() => env.Orders.UpdateAsync(created.Id, new UpdateOrderRequest(Note: "late"), Token));
        await Assert.ThrowsAsync<DomainException>(() => env.Orders.AddItemAsync(created.Id, Line(priced.StoreProductId, priced.BoxId, 1), Token));
        await Assert.ThrowsAsync<DomainException>(() => env.Orders.ChangeItemQuantityAsync(created.Id, itemId, new ChangeOrderItemQuantityRequest(3), Token));
        await Assert.ThrowsAsync<DomainException>(() => env.Orders.OverrideItemPriceAsync(created.Id, itemId, new OverrideOrderItemPriceRequest(1m, "x"), Token));
        await Assert.ThrowsAsync<DomainException>(() => env.Orders.RestoreItemPriceAsync(created.Id, itemId, Token));
        await Assert.ThrowsAsync<DomainException>(() => env.Orders.RemoveItemAsync(created.Id, itemId, Token));
        Assert.Equal(("CONFIRMED", 20_000m), ((await env.Orders.GetAsync(created.Id, Token)).Status, (await env.Orders.GetAsync(created.Id, Token)).TotalAmount));
    }

    // ----- Delivery address -----

    [RealDbFact]
    public async Task Delivery_needs_exactly_one_address_and_pickup_none_and_a_saved_address_must_be_the_farmers()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var priced = await NewProductAsync(env);
        var items = new[] { Line(priced.StoreProductId, priced.BottleId, 1) };
        var farmer = await NewFarmerAsync(env);
        var stranger = await NewFarmerAsync(env);
        var saved = new UserAddress(farmer.UserId, "Chị Sáu", "0987654321", "Ấp 1", "Cần Thơ", AddressType.Farm, "Xã Y", "Huyện Z");
        var foreign = new UserAddress(stranger.UserId, "Người lạ", "0987654322", "Ấp 9", "Cần Thơ", AddressType.Home);
        env.Context.UserAddresses.AddRange(saved, foreign);
        await env.Context.SaveChangesAsync(Token);
        CreateCounterOrderRequest Registered(string fulfillment, Guid? addressId = null, DeliveryAddressRequest? address = null) =>
            new("REGISTERED", "FULL_PAYMENT", fulfillment, items, farmer.ProfileId, null, null, addressId, address);

        var typed = await env.Orders.CreateAsync(Walk(items, fulfillment: "DELIVERY", address: Address()), Token);
        Assert.Equal(("Chú Tư", "0912345678", "Ấp 3", null, "Cần Thơ"),
            (typed.DeliveryAddress!.RecipientName, typed.DeliveryAddress.RecipientPhone, typed.DeliveryAddress.AddressLine, typed.DeliveryAddress.District, typed.DeliveryAddress.Province));
        var copied = await env.Orders.CreateAsync(Registered("DELIVERY", saved.Id), Token);
        Assert.Equal(("Chị Sáu", "Ấp 1", "Huyện Z"), (copied.DeliveryAddress!.RecipientName, copied.DeliveryAddress.AddressLine, copied.DeliveryAddress.District));

        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Orders.CreateAsync(Registered("DELIVERY"), Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Orders.CreateAsync(Registered("DELIVERY", saved.Id, Address()), Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Orders.CreateAsync(Registered("DELIVERY", foreign.Id), Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Orders.CreateAsync(Walk(items, fulfillment: "DELIVERY", addressId: saved.Id), Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Orders.CreateAsync(Registered("PICKUP", saved.Id), Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Orders.CreateAsync(Walk(items, address: Address()), Token));

        // Editing: the note and the address change together or alone; a pickup order takes no address.
        var edited = await env.Orders.UpdateAsync(
            typed.Id, new UpdateOrderRequest(DeliveryAddress: Address() with { AddressLine = "Ấp 4" }, Note: " Giao chiều "), Token);
        Assert.Equal(("Ấp 4", "Giao chiều"), (edited.DeliveryAddress!.AddressLine, edited.Note));
        var cleared = await env.Orders.UpdateAsync(typed.Id, new UpdateOrderRequest(Note: "  "), Token);
        Assert.Equal(("Ấp 4", null), (cleared.DeliveryAddress!.AddressLine, cleared.Note));
        var viaSaved = await env.Orders.UpdateAsync(copied.Id, new UpdateOrderRequest(AddressId: saved.Id), Token);
        Assert.Equal("Ấp 1", viaSaved.DeliveryAddress!.AddressLine);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Orders.UpdateAsync(copied.Id, new UpdateOrderRequest(AddressId: foreign.Id), Token));
        var pickup = await env.Orders.CreateAsync(Walk(items), Token);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Orders.UpdateAsync(pickup.Id, new UpdateOrderRequest(DeliveryAddress: Address()), Token));
    }

    // ----- Reading -----

    [RealDbFact]
    public async Task Orders_are_listed_newest_first_with_filters_and_search()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var priced = await NewProductAsync(env);
        var farmer = await NewFarmerAsync(env);
        var marker = Tag();
        var first = await env.Orders.CreateAsync(Walk([Line(priced.StoreProductId, priced.BottleId, 1)], name: $"Anh {marker}"), Token);
        var second = await env.Orders.CreateAsync(
            new CreateCounterOrderRequest(
                "REGISTERED", "CREDIT", "DELIVERY", [Line(priced.StoreProductId, priced.BottleId, 1), Line(priced.StoreProductId, priced.BoxId, 1)],
                farmer.ProfileId, null, null, null, Address()),
            Token);

        var byFarmer = await env.Orders.ListAsync(new OrderListRequest { FarmerProfileId = farmer.ProfileId }, Token);
        var item = Assert.Single(byFarmer.Items);
        Assert.Equal((second.Id, 2, "CREDIT", "DELIVERY", "REGISTERED", "COUNTER"), (item.Id, item.ItemCount, item.SettlementType, item.FulfillmentType, item.CustomerType, item.Source));

        Assert.Equal(first.Id, Assert.Single((await env.Orders.ListAsync(new OrderListRequest { Search = marker.ToUpperInvariant() }, Token)).Items).Id);
        Assert.Equal(second.Id, Assert.Single((await env.Orders.ListAsync(new OrderListRequest { Search = farmer.Phone }, Token)).Items).Id);
        Assert.Equal(second.Id, Assert.Single((await env.Orders.ListAsync(new OrderListRequest { Search = second.OrderNumber }, Token)).Items).Id);

        var mine = await env.Orders.ListAsync(new OrderListRequest { Status = "pending_confirmation", Source = "COUNTER" }, Token);
        Assert.Equal([second.Id, first.Id], mine.Items.Where(i => i.Id == first.Id || i.Id == second.Id).Select(i => i.Id));
        Assert.Empty((await env.Orders.ListAsync(new OrderListRequest { Status = "COMPLETED", FarmerProfileId = farmer.ProfileId }, Token)).Items);
        var today = BusinessCalendar.Today(Now);
        Assert.Empty((await env.Orders.ListAsync(new OrderListRequest { FromDate = today.AddDays(1) }, Token)).Items);
        Assert.Empty((await env.Orders.ListAsync(new OrderListRequest { ToDate = today.AddDays(-1), Search = marker }, Token)).Items);
        var firstPage = await env.Orders.ListAsync(new OrderListRequest { FromDate = today, ToDate = today, PageSize = 1 }, Token);
        Assert.Equal((second.Id, 1, true), (Assert.Single(firstPage.Items).Id, firstPage.Items.Count, firstPage.TotalCount >= 2));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Orders.GetAsync(Guid.NewGuid(), Token));
    }
}
