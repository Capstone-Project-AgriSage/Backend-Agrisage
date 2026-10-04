using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Placeholders;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Pricing;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Services;
using AgriSage.Application.Features.Reports;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Pricing.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Returns.Entities;
using AgriSage.Domain.Features.Returns.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Reports;

// REAL PostgreSQL (opt-in AGRISAGE_DB_TESTS=1): the sales report (F1.8), always rolled back. Sales are posted in March 2031 with
// a controllable clock, so the report of that period holds only this test's rows whatever agrisage-dev contains.
// Active walk-in lists and default customer groups already there are switched off inside the transaction first.
[Collection(RealDb.WalkInPriceListCollection)]
public class SalesReportDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Tag() => Guid.NewGuid().ToString("N")[..10];

    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    // Vietnam is UTC+7: 2031-03-05 16:59 UTC is 23:59 on the 5th, 17:00 UTC is 00:00 on the 6th.
    private static DateTimeOffset Utc(int day, int hour, int minute = 0) => new(2031, 3, day, hour, minute, 0, TimeSpan.Zero);

    private static readonly DateOnly Mar5 = new(2031, 3, 5);
    private static readonly DateOnly Mar6 = new(2031, 3, 6);

    private sealed class MutableClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class Env : IAsyncDisposable
    {
        public Env(AgriSageDbContext context, RealDb.MutableUser user)
        {
            Context = context;
            User = user;
            var errors = new NpgsqlErrorClassifier();
            var realClock = new DateTimeProvider();
            var audit = new AuditTrail(context, user, Clock);
            var locks = new RowLockService(context);
            Categories = new CategoryService(context, errors);
            Products = new ProductService(context, errors);
            StoreProducts = new StoreProductService(context, errors);
            PriceLists = new PriceListService(context, realClock, errors, new AuditTrail(context, user, realClock));
            var ledger = new OrderPrepaymentLedger(context);
            var queries = new OrderQueries(context);
            var builder = new OrderBuilder(context, new PriceResolver(context), user, Clock, audit);
            var confirmer = new OrderConfirmer(context, locks, new TemporaryOrderSettlementGuard(), user, Clock, audit);
            var posting = new FulfillmentPostingService(context, locks, new TemporaryFulfillmentFinancialPosting(ledger));
            Orders = new OrderService(context, builder, queries, user, Clock, errors, audit);
            Confirmation = new OrderConfirmationService(context, locks, confirmer, queries, Clock, errors, audit);
            Pickup = new OrderPickupService(context, locks, posting, new TemporaryOrderSettlementGuard(), new OrderPaymentCancellation(context, locks, Clock, audit), queries, user, Clock, audit);
            Sales = new CounterSaleService(
                context, builder, new PaymentAllocator(Clock, new TemporaryCreditReservationAdjuster(), new TemporaryDebtRepaymentPosting()),
                confirmer, posting, queries, new PaymentQueries(context), user, Clock, errors, audit);
            Report = new SalesReportService(context);
        }

        public MutableClock Clock { get; } = new();

        public AgriSageDbContext Context { get; }

        public RealDb.MutableUser User { get; }

        public CategoryService Categories { get; }

        public ProductService Products { get; }

        public StoreProductService StoreProducts { get; }

        public PriceListService PriceLists { get; }

        public OrderService Orders { get; }

        public OrderConfirmationService Confirmation { get; }

        public OrderPickupService Pickup { get; }

        public CounterSaleService Sales { get; }

        public SalesReportService Report { get; }

        public User Actor { get; set; } = null!;

        public User OtherStaff { get; set; } = null!;

        public Guid StoreId { get; set; }

        public Guid Bottle { get; set; }

        public Guid Box { get; set; }

        public Guid WalkInListId { get; set; }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed record Sku(Guid StoreProductId, Guid BottleId, Guid BoxId, string Code, string Name, InventoryLot Lot);

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
        var roleId = await RoleIdAsync(context, RoleCode.SalesStaff);
        env.Actor = new User(roleId, "An bán hàng", "hash", $"{Tag()}@example.test", null);
        env.OtherStaff = new User(roleId, "Bình bán hàng", "hash", $"{Tag()}@example.test", null);
        context.Users.AddRange(env.Actor, env.OtherStaff);
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

        var walkIn = await env.PriceLists.CreateAsync(new PriceListRequest($"PL-{Tag()}", "Bảng giá thử", Now.AddDays(-1), null, true), Token);
        await env.PriceLists.ActivateAsync(walkIn.Id, Token);
        env.WalkInListId = walkIn.Id;

        return env;
    }

    // Bottle priced 10 000 and Box of 6 priced 55 000, one lot with 200 base units at the weighted cost of 5 000 each.
    private static async Task<Sku> NewSkuAsync(Env env, string label)
    {
        var tag = Tag();
        var category = await env.Categories.CreateAsync(new CreateCategoryRequest($"C-{tag}", $"Category {tag}", null, 0, null), Token);
        var product = await env.Products.CreateAsync(
            new CreateProductRequest(
                $"SKU-{tag}", $"{label} {tag}", category.Id,
                [
                    new PackagingRequest(env.Bottle, 1, true, false, true, "Bottle"),
                    new PackagingRequest(env.Box, 6, false, true, true, "Box of 6")
                ]),
            Token);
        var storeProduct = await env.StoreProducts.CreateAsync(new CreateStoreProductRequest(product.Id), Token);
        var bottleId = product.Packagings.Single(p => p.UnitId == env.Bottle).Id;
        var boxId = product.Packagings.Single(p => p.UnitId == env.Box).Id;
        await env.PriceLists.UpsertItemsAsync(
            env.WalkInListId,
            new UpsertPriceListItemsRequest(
            [
                new PriceListItemInput(storeProduct.Id, bottleId, 10_000m),
                new PriceListItemInput(storeProduct.Id, boxId, 55_000m)
            ]),
            Token);

        var lot = new InventoryLot(storeProduct.Id, $"L-{tag}", null, new DateOnly(2032, 1, 1));
        lot.ReceiveStock(200, 5_000m);
        env.Context.InventoryLots.Add(lot);
        await env.Context.SaveChangesAsync(Token);

        return new Sku(storeProduct.Id, bottleId, boxId, product.Sku, $"{label} {tag}", lot);
    }

    private static async Task<FarmerProfile> NewFarmerAsync(Env env, CustomerGroup? group = null)
    {
        var user = new User(await RoleIdAsync(env.Context, RoleCode.Farmer), "Nông dân thử", "hash", $"{Tag()}@example.test", "09" + Random.Shared.Next(10_000_000, 99_999_999));
        var farmer = new FarmerProfile(user.Id);
        env.Context.AddRange(user, farmer);
        if (group is not null)
        {
            env.Context.CustomerGroupAssignments.Add(new CustomerGroupAssignment(farmer.Id, group.Id, Now.AddDays(-1), env.Actor.Id));
        }

        await env.Context.SaveChangesAsync(Token);

        return farmer;
    }

    private static CounterSaleItemRequest Line(Sku sku, long quantity, decimal? price = null, string? reason = null) =>
        new(sku.StoreProductId, sku.BottleId, quantity, price, reason, [new CounterSaleLotRequest(sku.Lot.Id, quantity)]);

    // The sales of the scenario (all in March 2031, Vietnam days):
    //  A  walk-in, staff An: 3 bottles @10 000 + 2 boxes @55 000 of product P = 140 000, handed over in two parts —
    //     3 bottles + 1 box (9 base units) on the 5th, the other box (6) on the 6th. Priced per base unit at 140 000 / 15.
    //  B  farmer of group G, staff An, 10 bottles of P at an overridden 8 000 = 80 000, on the 5th at 23:59 Vietnam time.
    //  C  farmer without a group, staff Bình, 5 bottles of Q @10 000 = 50 000, on the 6th at 00:00 Vietnam time.
    //  Cost is 5 000 per base unit. A return of 2 bottles of B (16 000) is completed on the 6th.
    private sealed record Scenario(Env Env, Sku P, Sku Q, OrderResponse A, OrderResponse B, OrderResponse C, CustomerGroup Group);

    private static async Task<Scenario> BuildScenarioAsync(Env env)
    {
        var p = await NewSkuAsync(env, "Phân P");
        var q = await NewSkuAsync(env, "Thuốc Q");
        var group = new CustomerGroup(env.StoreId, $"G{Tag()}", "Đại lý cấp 1");
        env.Context.CustomerGroups.Add(group);
        await env.Context.SaveChangesAsync(Token);
        var farmerB = await NewFarmerAsync(env, group);
        var farmerC = await NewFarmerAsync(env);

        env.Clock.UtcNow = Utc(5, 5);
        var a = await env.Orders.CreateAsync(
            new CreateCounterOrderRequest(
                "WALK_IN", "FULL_PAYMENT", "PICKUP",
                [new OrderItemRequest(p.StoreProductId, p.BottleId, 3), new OrderItemRequest(p.StoreProductId, p.BoxId, 2)]),
            Token);
        await env.Confirmation.ConfirmAsync(a.Id, Token);
        var bottleItem = a.Items.Single(i => i.ProductPackagingId == p.BottleId).Id;
        var boxItem = a.Items.Single(i => i.ProductPackagingId == p.BoxId).Id;
        await env.Pickup.PickupAsync(
            a.Id,
            new PickupRequest(
            [
                new PickupItemRequest(bottleItem, [new PickupLotRequest(p.Lot.Id, 3)]),
                new PickupItemRequest(boxItem, [new PickupLotRequest(p.Lot.Id, 6)])
            ]),
            Token);
        env.Clock.UtcNow = Utc(6, 5);
        await env.Pickup.PickupAsync(a.Id, new PickupRequest([new PickupItemRequest(boxItem, [new PickupLotRequest(p.Lot.Id, 6)])]), Token);

        env.Clock.UtcNow = Utc(5, 16, 59);
        var b = (await env.Sales.SellAsync(
            new CounterSaleRequest("REGISTERED", [Line(p, 10, 8_000m, "Khách quen")], farmerB.Id), Token)).Order;

        env.User.UserId = env.OtherStaff.Id;
        env.Clock.UtcNow = Utc(5, 17);
        var c = (await env.Sales.SellAsync(new CounterSaleRequest("REGISTERED", [Line(q, 5)], farmerC.Id), Token)).Order;
        env.User.UserId = env.Actor.Id;

        // A completed return of 2 bottles of B, on the 6th.
        var orderB = await env.Context.Orders.Include(o => o.Items).SingleAsync(o => o.Id == b.Id, Token);
        var movement = await env.Context.StockMovements.Include(m => m.Items).SingleAsync(m => m.OrderId == b.Id, Token);
        var salesReturn = new SalesReturn(orderB, $"SR-{Tag()}", env.Actor.Id, Utc(6, 3));
        var line = salesReturn.AddItem(
            orderB, orderB.Items.Single(), 2, 0, p.Lot.Id, "QUALITY_ISSUE",
            originalStockMovementItemId: movement.Items.Single().Id, originalCogsUnitCost: 5_000m);
        salesReturn.Approve(env.Actor.Id, Utc(6, 3));
        salesReturn.MarkReceived(env.Actor.Id, Utc(6, 3));
        salesReturn.InspectItem(line.Id, ReturnConditionStatus.Damaged);
        salesReturn.CompleteInspection(env.Actor.Id, Utc(6, 3), line.ReturnValue);
        salesReturn.Complete(Utc(6, 4));
        env.Context.SalesReturns.Add(salesReturn);
        await env.Context.SaveChangesAsync(Token);
        Assert.Equal(16_000m, line.ReturnValue);

        return new Scenario(env, p, q, a, b, c, group);
    }

    private static Task<SalesReportResponse> ReportAsync(Env env, DateOnly from, DateOnly to, string? groupBy = null) =>
        env.Report.GetAsync(new SalesReportRequest { FromDate = from, ToDate = to, GroupBy = groupBy }, Token);

    private static (string Key, int Orders, decimal Fulfilled, decimal Cost, decimal Profit, decimal Returns, decimal Net) Shape(SalesReportRow r) =>
        (r.Key, r.OrderCount, r.FulfilledValue, r.CostOfGoods, r.GrossProfit, r.ReturnValue, r.NetSales);

    // ----- Report -----

    [RealDbFact]
    public async Task Revenue_is_recognized_when_goods_leave_and_every_total_adds_up_by_day()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        await BuildScenarioAsync(env);

        var report = await ReportAsync(env, Mar5, Mar6);

        // 5th: A's first part 84 000 (9 × 140 000 / 15) + B 80 000. 6th (Vietnam): A's rest 56 000 + C 50 000, minus the return.
        Assert.Equal((Mar5, Mar6, "DAY"), (report.FromDate, report.ToDate, report.GroupBy));
        Assert.Equal(
            [("2031-03-05", 2, 164_000m, 95_000m, 69_000m, 0m, 164_000m), ("2031-03-06", 2, 106_000m, 55_000m, 51_000m, 16_000m, 90_000m)],
            report.Rows.Select(Shape));
        Assert.All(report.Rows, r => Assert.Equal(r.Key, r.Label));
        // A is in both rows but counted once in the totals; 140 000 + 80 000 + 50 000.
        Assert.Equal((3, 270_000m, 150_000m, 120_000m, 16_000m, 254_000m),
            (report.Totals.OrderCount, report.Totals.FulfilledValue, report.Totals.CostOfGoods, report.Totals.GrossProfit, report.Totals.ReturnValue, report.Totals.NetSales));

        // The default grouping is DAY; a period of one day sees only that day (the return of the 6th is not in it).
        Assert.Equal("DAY", (await ReportAsync(env, Mar5, Mar5)).GroupBy);
        var fifth = await ReportAsync(env, Mar5, Mar5, " day ");
        Assert.Equal(("2031-03-05", 164_000m, 0m, 164_000m), (Assert.Single(fifth.Rows).Key, fifth.Totals.FulfilledValue, fifth.Totals.ReturnValue, fifth.Totals.NetSales));
        var sixth = await ReportAsync(env, Mar6, Mar6);
        Assert.Equal((106_000m, 16_000m), (sixth.Totals.FulfilledValue, sixth.Totals.ReturnValue));
        Assert.Empty((await ReportAsync(env, new DateOnly(2031, 3, 7), new DateOnly(2031, 3, 10))).Rows);
    }

    [RealDbFact]
    public async Task The_report_groups_by_product_staff_and_customer_group_with_the_same_totals()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var scenario = await BuildScenarioAsync(env);

        var byProduct = await ReportAsync(env, Mar5, Mar6, "PRODUCT");
        // P sold 140 000 (A) + 80 000 (B) at 5 000 a unit for 15 + 10 base units; Q 50 000.
        Assert.Equal(
            [(scenario.P.StoreProductId.ToString(), 2, 220_000m, 125_000m, 95_000m, 16_000m, 204_000m), (scenario.Q.StoreProductId.ToString(), 1, 50_000m, 25_000m, 25_000m, 0m, 50_000m)],
            byProduct.Rows.Select(Shape));
        Assert.Equal([$"{scenario.P.Name} ({scenario.P.Code})", $"{scenario.Q.Name} ({scenario.Q.Code})"], byProduct.Rows.Select(r => r.Label));

        var byStaff = await ReportAsync(env, Mar5, Mar6, "staff");
        Assert.Equal(
            [(env.Actor.Id.ToString(), 2, 220_000m, 125_000m, 95_000m, 16_000m, 204_000m), (env.OtherStaff.Id.ToString(), 1, 50_000m, 25_000m, 25_000m, 0m, 50_000m)],
            byStaff.Rows.Select(Shape));
        Assert.Equal(["An bán hàng", "Bình bán hàng"], byStaff.Rows.Select(r => r.Label));

        var byGroup = await ReportAsync(env, Mar5, Mar6, "CUSTOMER_GROUP");
        Assert.Equal(
            [("WALK_IN", 1, 140_000m, 75_000m, 65_000m, 0m, 140_000m), (scenario.Group.Id.ToString(), 1, 80_000m, 50_000m, 30_000m, 16_000m, 64_000m), ("UNGROUPED", 1, 50_000m, 25_000m, 25_000m, 0m, 50_000m)],
            byGroup.Rows.Select(Shape));
        Assert.Equal(["Khách lẻ", scenario.Group.Name, "Chưa phân nhóm"], byGroup.Rows.Select(r => r.Label));

        // Whatever the grouping, the totals are the same.
        foreach (var report in new[] { byProduct, byStaff, byGroup })
        {
            Assert.Equal((3, 270_000m, 150_000m, 120_000m, 16_000m, 254_000m),
                (report.Totals.OrderCount, report.Totals.FulfilledValue, report.Totals.CostOfGoods, report.Totals.GrossProfit, report.Totals.ReturnValue, report.Totals.NetSales));
            Assert.Equal(report.Totals.FulfilledValue, report.Rows.Sum(r => r.FulfilledValue));
        }
    }

    [RealDbFact]
    public async Task A_fully_handed_over_order_adds_up_to_its_total_and_cancelled_or_pending_orders_add_nothing()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var p = await NewSkuAsync(env, "Phân P");
        env.Clock.UtcNow = Utc(8, 5);

        // A pending order, one confirmed but not handed over, and one cancelled after confirmation: no revenue.
        await env.Orders.CreateAsync(
            new CreateCounterOrderRequest("WALK_IN", "FULL_PAYMENT", "PICKUP", [new OrderItemRequest(p.StoreProductId, p.BottleId, 7)]), Token);
        var confirmed = await env.Orders.CreateAsync(
            new CreateCounterOrderRequest("WALK_IN", "FULL_PAYMENT", "PICKUP", [new OrderItemRequest(p.StoreProductId, p.BottleId, 7)]), Token);
        await env.Confirmation.ConfirmAsync(confirmed.Id, Token);

        var empty = await ReportAsync(env, new DateOnly(2031, 3, 8), new DateOnly(2031, 3, 8));
        Assert.Empty(empty.Rows);
        Assert.Equal((0, 0m, 0m, 0m), (empty.Totals.OrderCount, empty.Totals.FulfilledValue, empty.Totals.CostOfGoods, empty.Totals.NetSales));

        // A three-packaging-free order of odd quantities: 7 bottles sold = 70 000 exactly.
        await env.Pickup.PickupAsync(
            confirmed.Id, new PickupRequest([new PickupItemRequest(confirmed.Items.Single().Id, [new PickupLotRequest(p.Lot.Id, 7)])]), Token);
        var done = await ReportAsync(env, new DateOnly(2031, 3, 8), new DateOnly(2031, 3, 8));
        Assert.Equal((1, 70_000m, 35_000m, 35_000m), (done.Totals.OrderCount, done.Totals.FulfilledValue, done.Totals.CostOfGoods, done.Totals.GrossProfit));
    }
}
