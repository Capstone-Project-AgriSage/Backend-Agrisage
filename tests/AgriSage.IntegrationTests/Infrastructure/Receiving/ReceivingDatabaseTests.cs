using System.Text.RegularExpressions;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.GoodsReceipts;
using AgriSage.Application.Features.GoodsReceipts.Import;
using AgriSage.Application.Features.Inventory;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Dtos.Responses;
using AgriSage.Application.Features.Products.Services;
using AgriSage.Application.Features.Suppliers;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using AgriSage.Infrastructure.Spreadsheets;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Receiving;

// REAL PostgreSQL (opt-in AGRISAGE_DB_TESTS=1): suppliers, goods receipts, lots, weighted average cost and the
// stock movement ledger against the real schema, always rolled back.
public class ReceivingDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Tag() => Guid.NewGuid().ToString("N")[..10];

    private static DateOnly Today => BusinessCalendar.Today(DateTimeOffset.UtcNow);

    private sealed class Env(RealDb.Session session, AgriSageDbContext context, User actor) : IAsyncDisposable
    {
        public RealDb.Session Session { get; } = session;

        public AgriSageDbContext Context { get; } = context;

        public User Actor { get; } = actor;

        public Guid StoreId { get; set; }

        public Guid BottleUnit { get; set; }

        public Guid BoxUnit { get; set; }

        public SupplierService Suppliers { get; private set; } = null!;

        public GoodsReceiptService Receipts { get; private set; } = null!;

        public GoodsReceiptImportService Import { get; private set; } = null!;

        public InventoryService Inventory { get; private set; } = null!;

        public ProductService Products { get; private set; } = null!;

        public StoreProductService StoreProducts { get; private set; } = null!;

        public CategoryService Categories { get; private set; } = null!;

        public RowLockService Locks { get; private set; } = null!;

        public ValueTask DisposeAsync() => Context.DisposeAsync();

        public void BuildServices(RealDb.MutableUser user)
        {
            var errors = new NpgsqlErrorClassifier();
            var clock = new DateTimeProvider();
            Locks = new RowLockService(Context);
            Suppliers = new SupplierService(Context, errors);
            var confirmer = new GoodsReceiptConfirmer(Context, Locks, user, clock, errors);
            Receipts = new GoodsReceiptService(Context, user, clock, errors, confirmer);
            Import = new GoodsReceiptImportService(Context, clock, new ClosedXmlReceiptSpreadsheet(), Receipts);
            Inventory = new InventoryService(Context, clock, Locks);
            Products = new ProductService(Context, errors);
            StoreProducts = new StoreProductService(Context, errors);
            Categories = new CategoryService(Context, errors);
        }
    }

    private sealed record Stocked(ProductResponse Product, Guid StoreProductId, Guid BoxId);

    private static async Task<Env> PrepareAsync(RealDb.Session session)
    {
        var context = session.NewContext();

        async Task<Role> RoleAsync(RoleCode code)
        {
            var role = await context.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Code == code, Token);
            if (role is null)
            {
                role = new Role(code, code.ToString());
                context.Roles.Add(role);
                await context.SaveChangesAsync(Token);
            }

            return role;
        }

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

        var admin = await RoleAsync(RoleCode.Admin);
        var actor = new User(admin.Id, "Receiving Tester", "hash", $"{Tag()}@example.test", null);
        context.Users.Add(actor);
        await context.SaveChangesAsync(Token);

        var env = new Env(session, context, actor)
        {
            BottleUnit = await UnitAsync("BOTTLE", "Bottle"),
            BoxUnit = await UnitAsync("BOX", "Box")
        };

        var storeId = await context.Stores.Where(s => s.Status == StoreStatus.Active).Select(s => s.Id).FirstOrDefaultAsync(Token);
        if (storeId == Guid.Empty)
        {
            var store = new Store($"T{Tag()}", "Test store", "1 Test Street", "Test");
            context.Stores.Add(store);
            await context.SaveChangesAsync(Token);
            storeId = store.Id;
        }

        env.StoreId = storeId;
        env.BuildServices(new RealDb.MutableUser { UserId = actor.Id, Role = "ADMIN" });
        return env;
    }

    // Bottle = base unit (sold, not bought); Box of 6 = purchase unit.
    private static async Task<Stocked> NewStockedProductAsync(Env env, bool lotTracked = true, bool expiryRequired = true)
    {
        var tag = Tag();
        var category = await env.Categories.CreateAsync(new CreateCategoryRequest($"C-{tag}", $"Category {tag}", null, 0, null), Token);
        var product = await env.Products.CreateAsync(
            new CreateProductRequest(
                $"SKU-{tag}", $"Product {tag}", category.Id,
                [new PackagingRequest(env.BottleUnit, 1, true, false, true), new PackagingRequest(env.BoxUnit, 6, false, true, true)],
                RequiresLotTracking: lotTracked,
                RequiresExpiryDate: expiryRequired),
            Token);
        var storeProduct = await env.StoreProducts.CreateAsync(new CreateStoreProductRequest(product.Id), Token);

        return new Stocked(product, storeProduct.Id, product.Packagings.Single(p => !p.IsBaseUnit).Id);
    }

    private static async Task<SupplierResponse> NewSupplierAsync(Env env, string? code = null) =>
        await env.Suppliers.CreateAsync(new SupplierRequest($"Supplier {Tag()}", code), Token);

    private static GoodsReceiptItemRequest Line(
        Stocked stocked, long boxes, decimal costPerBox, string? lot = "LOT-A", DateOnly? expiry = null, string? note = null) =>
        new(stocked.StoreProductId, stocked.BoxId, boxes, costPerBox, lot, null, expiry ?? Today.AddYears(1), note);

    private static async Task<GoodsReceiptResponse> DraftAsync(Env env, SupplierResponse supplier, params GoodsReceiptItemRequest[] items) =>
        await env.Receipts.CreateAsync(new CreateGoodsReceiptRequest(supplier.Id, Items: items), Token);

    // ----- Suppliers -----

    [RealDbFact]
    public async Task Supplier_codes_are_unique_ignoring_case_and_used_suppliers_cannot_be_deleted()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var code = $"S-{Tag()}";
        var supplier = await NewSupplierAsync(env, code);

        await Assert.ThrowsAsync<ConflictException>(() => env.Suppliers.CreateAsync(new SupplierRequest("Other", code.ToLowerInvariant()), Token));
        var other = await env.Suppliers.CreateAsync(new SupplierRequest("Other"), Token);
        await Assert.ThrowsAsync<ConflictException>(() => env.Suppliers.UpdateAsync(other.Id, new SupplierRequest("Other", code), Token));
        var updated = await env.Suppliers.UpdateAsync(
            supplier.Id, new SupplierRequest("  Renamed ", code, Email: "Sales@Supplier.VN", Province: "Can Tho"), Token);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal("sales@supplier.vn", updated.Email);

        var stocked = await NewStockedProductAsync(env);
        await DraftAsync(env, supplier, Line(stocked, 1, 100_000m));
        await Assert.ThrowsAsync<ConflictException>(() => env.Suppliers.DeleteAsync(supplier.Id, Token));

        await env.Suppliers.SetActiveAsync(supplier.Id, false, Token);
        Assert.False((await env.Suppliers.GetAsync(supplier.Id, Token)).IsActive);
        await env.Suppliers.DeleteAsync(other.Id, Token);
        await Assert.ThrowsAsync<NotFoundException>(() => env.Suppliers.GetAsync(other.Id, Token));
        var listed = await env.Suppliers.ListAsync(new SupplierListRequest { Search = code, IsActive = false }, Token);
        Assert.Equal(supplier.Id, Assert.Single(listed.Items).Id);
    }

    // ----- Draft receipts -----

    [RealDbFact]
    public async Task Draft_receipts_get_generated_numbers_the_caller_as_receiver_and_totals()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var supplier = await NewSupplierAsync(env);
        var stocked = await NewStockedProductAsync(env);

        var first = await DraftAsync(env, supplier, Line(stocked, 2, 120_000m), Line(stocked, 1, 50_000m, "LOT-B"));
        var second = await DraftAsync(env, supplier);

        Assert.Matches($"^GR-{Today:yyyyMMdd}-\\d{{4}}$", first.ReceiptNumber);
        var firstSequence = DocumentNumbers.SequenceOf(first.ReceiptNumber, "GR", Today);
        Assert.Equal(firstSequence + 1, DocumentNumbers.SequenceOf(second.ReceiptNumber, "GR", Today));
        Assert.Equal("DRAFT", first.Status);
        Assert.Equal("MANUAL", first.SourceType);
        Assert.Equal(env.Actor.Id, first.ReceivedBy);
        Assert.Equal(290_000m, first.TotalAmount);
        Assert.Equal(2, first.Items.Count);
        Assert.Equal(12, first.Items.First(i => i.SupplierLotNumber == "LOT-A").BaseQuantity);
        Assert.Equal(20_000m, first.Items.First(i => i.SupplierLotNumber == "LOT-A").BaseUnitCost);
        Assert.All(first.Items, i => Assert.Null(i.InventoryLotId));
        Assert.Null(first.StockMovementId);
    }

    [RealDbFact]
    public async Task Draft_lines_can_be_added_changed_and_removed_and_totals_follow()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var supplier = await NewSupplierAsync(env);
        var stocked = await NewStockedProductAsync(env);
        var receipt = await DraftAsync(env, supplier);

        var added = await env.Receipts.AddItemAsync(receipt.Id, Line(stocked, 2, 100_000m), Token);
        var itemId = Assert.Single(added.Items).Id;
        Assert.Equal(200_000m, added.TotalAmount);

        var updated = await env.Receipts.UpdateItemAsync(
            receipt.Id, itemId, new UpdateGoodsReceiptItemRequest(3, 100_000m, "LOT-A", null, Today.AddYears(2)), Token);
        Assert.Equal(300_000m, updated.TotalAmount);
        Assert.Equal(18, updated.Items.Single().BaseQuantity);

        var header = await env.Receipts.UpdateHeaderAsync(
            receipt.Id, new UpdateGoodsReceiptRequest(supplier.Id, DateTimeOffset.UtcNow.AddHours(-2), " INV-1 ", Today, "note"), Token);
        Assert.Equal("INV-1", header.SupplierInvoiceNumber);

        var removed = await env.Receipts.RemoveItemAsync(receipt.Id, itemId, Token);
        Assert.Empty(removed.Items);
        Assert.Equal(0m, removed.TotalAmount);
        await Assert.ThrowsAsync<NotFoundException>(() => env.Receipts.RemoveItemAsync(receipt.Id, Guid.NewGuid(), Token));
    }

    [RealDbFact]
    public async Task Invalid_lines_headers_and_suppliers_are_rejected()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var supplier = await NewSupplierAsync(env);
        var stocked = await NewStockedProductAsync(env);
        var receipt = await DraftAsync(env, supplier);
        var bottle = stocked.Product.Packagings.Single(p => p.IsBaseUnit);

        // Bottle is not a purchase unit.
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Receipts.AddItemAsync(
            receipt.Id, new GoodsReceiptItemRequest(stocked.StoreProductId, bottle.Id, 1, 1m, "L", null, Today.AddYears(1)), Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Receipts.AddItemAsync(receipt.Id, Line(stocked, 1, 1m, lot: null), Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Receipts.AddItemAsync(
            receipt.Id, new GoodsReceiptItemRequest(stocked.StoreProductId, stocked.BoxId, 1, 1m, "L", null, null), Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Receipts.AddItemAsync(
            receipt.Id, Line(stocked, 1, 1m, expiry: Today.AddDays(-1)), Token));
        await Assert.ThrowsAsync<DomainException>(() => env.Receipts.AddItemAsync(receipt.Id, Line(stocked, 1, 0.005m), Token));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Receipts.AddItemAsync(
            receipt.Id, new GoodsReceiptItemRequest(Guid.NewGuid(), stocked.BoxId, 1, 1m, "L", null, Today.AddYears(1)), Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Receipts.UpdateHeaderAsync(
            receipt.Id, new UpdateGoodsReceiptRequest(supplier.Id, DateTimeOffset.UtcNow.AddDays(2)), Token));

        await env.Suppliers.SetActiveAsync(supplier.Id, false, Token);
        await Assert.ThrowsAsync<BusinessRuleException>(() => DraftAsync(env, supplier));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Receipts.CreateAsync(new CreateGoodsReceiptRequest(Guid.NewGuid()), Token));

        await env.Products.ChangeStatusAsync(stocked.Product.Id, new ChangeProductStatusRequest("DISCONTINUED"), Token);
        var otherSupplier = await NewSupplierAsync(env);
        await Assert.ThrowsAsync<BusinessRuleException>(() => DraftAsync(env, otherSupplier, Line(stocked, 1, 1m)));
    }

    [RealDbFact]
    public async Task Cancel_and_delete_work_on_drafts_only_and_create_no_movement()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var supplier = await NewSupplierAsync(env);
        var stocked = await NewStockedProductAsync(env);
        var toCancel = await DraftAsync(env, supplier, Line(stocked, 1, 100_000m));
        var toDelete = await DraftAsync(env, supplier);

        var cancelled = await env.Receipts.CancelAsync(toCancel.Id, new CancelGoodsReceiptRequest("Wrong supplier"), Token);
        await env.Receipts.DeleteAsync(toDelete.Id, Token);

        Assert.Equal("CANCELLED", cancelled.Status);
        Assert.Equal("Wrong supplier", cancelled.CancelReason);
        Assert.Equal(env.Actor.Id, cancelled.CancelledBy);
        await Assert.ThrowsAsync<NotFoundException>(() => env.Receipts.GetAsync(toDelete.Id, Token));
        await Assert.ThrowsAsync<DomainException>(() => env.Receipts.AddItemAsync(toCancel.Id, Line(stocked, 1, 1m), Token));
        await Assert.ThrowsAsync<DomainException>(() => env.Receipts.DeleteAsync(toCancel.Id, Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Receipts.ConfirmAsync(toCancel.Id, Token));
        Assert.Equal(0, await env.Context.StockMovements.CountAsync(m => m.GoodsReceiptId == toCancel.Id, Token));
    }

    // ----- Confirmation -----

    [RealDbFact]
    public async Task Confirming_creates_the_lot_adds_stock_and_posts_a_stock_in_movement()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var supplier = await NewSupplierAsync(env);
        var stocked = await NewStockedProductAsync(env);
        var expiry = Today.AddYears(1);
        var draft = await DraftAsync(env, supplier, Line(stocked, 2, 120_000m, "LOT-A", expiry, "first line"));

        var confirmed = await env.Receipts.ConfirmAsync(draft.Id, Token);

        Assert.Equal("CONFIRMED", confirmed.Status);
        Assert.Equal(env.Actor.Id, confirmed.ConfirmedBy);
        Assert.NotNull(confirmed.ConfirmedAt);
        var item = Assert.Single(confirmed.Items);
        Assert.NotNull(item.InventoryLotId);
        Assert.NotNull(confirmed.StockMovementId);

        var lot = await env.Inventory.GetLotAsync(item.InventoryLotId!.Value, Token);
        Assert.Equal("LOT-A", lot.LotNumber);
        Assert.Equal(expiry, lot.ExpiryDate);
        Assert.Equal("ACTIVE", lot.Status);
        Assert.Equal(12, lot.QuantityOnHand);
        Assert.Equal(0, lot.QuantityReserved);
        Assert.Equal(12, lot.QuantityAvailable);
        Assert.Equal(240_000m, lot.TotalCostValue);
        Assert.Equal(20_000m, lot.AverageUnitCost);
        Assert.False(lot.IsExpired);

        var movement = await env.Inventory.GetMovementAsync(confirmed.StockMovementId!.Value, Token);
        Assert.Equal("STOCK_IN", movement.MovementType);
        Assert.Equal("POSTED", movement.Status);
        Assert.Equal(draft.Id, movement.GoodsReceiptId);
        Assert.Equal(env.Actor.Id, movement.PostedBy);
        Assert.Matches($"^SM-{Today:yyyyMMdd}-\\d{{4}}$", movement.MovementNumber);
        var movementItem = Assert.Single(movement.Items);
        Assert.Equal(12, movementItem.QuantityDeltaBase);
        Assert.Equal(20_000m, movementItem.UnitCostSnapshot);
        Assert.Equal(240_000m, movementItem.TotalCostSnapshot);
        Assert.Equal(12, movementItem.QuantityOnHandAfter);
        Assert.Equal(240_000m, movementItem.TotalCostValueAfter);
        Assert.Equal(lot.Id, movementItem.InventoryLotId);
        Assert.Equal("first line", movementItem.Note);

        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Receipts.ConfirmAsync(draft.Id, Token));
        await Assert.ThrowsAsync<DomainException>(() => env.Receipts.AddItemAsync(draft.Id, Line(stocked, 1, 1m), Token));
        await Assert.ThrowsAsync<DomainException>(() => env.Receipts.DeleteAsync(draft.Id, Token));
        await Assert.ThrowsAsync<DomainException>(() => env.Receipts.CancelAsync(draft.Id, new CancelGoodsReceiptRequest(), Token));
    }

    // Database design §25: 100 @ 100,000 + 100 @ 110,000 → 200 @ 105,000 (scaled to boxes of 6 here).
    [RealDbFact]
    public async Task Receiving_the_same_logical_lot_again_updates_the_weighted_average_cost()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var supplier = await NewSupplierAsync(env);
        var stocked = await NewStockedProductAsync(env);
        var expiry = Today.AddYears(1);

        var firstReceipt = await env.Receipts.ConfirmAsync(
            (await DraftAsync(env, supplier, Line(stocked, 10, 600_000m, "LOT-A", expiry))).Id, Token);
        var lotId = firstReceipt.Items.Single().InventoryLotId!.Value;
        // Same lot written differently (case, spaces): still the same logical lot.
        var secondReceipt = await env.Receipts.ConfirmAsync(
            (await DraftAsync(env, supplier, Line(stocked, 10, 660_000m, " lot-a ", expiry))).Id, Token);

        Assert.Equal(lotId, secondReceipt.Items.Single().InventoryLotId);
        var lot = await env.Inventory.GetLotAsync(lotId, Token);
        Assert.Equal("LOT-A", lot.LotNumber);
        Assert.Equal(120, lot.QuantityOnHand);
        Assert.Equal(12_600_000m, lot.TotalCostValue);
        Assert.Equal(105_000m, lot.AverageUnitCost);
        Assert.Equal(1, await env.Context.InventoryLots.CountAsync(l => l.StoreProductId == stocked.StoreProductId, Token));
        var version = await env.Context.InventoryLotBalances.AsNoTracking().Where(b => b.InventoryLotId == lotId).Select(b => b.Version).SingleAsync(Token);
        Assert.Equal(1, version);

        var movements = await env.Inventory.ListMovementsAsync(
            new StockMovementListRequest { GoodsReceiptId = secondReceipt.Id }, Token);
        Assert.Equal(secondReceipt.StockMovementId, Assert.Single(movements.Items).Id);
    }

    [RealDbFact]
    public async Task Different_expiry_dates_or_lot_numbers_are_separate_lots_and_lines_of_one_lot_share_it()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var supplier = await NewSupplierAsync(env);
        var stocked = await NewStockedProductAsync(env);
        var expiry = Today.AddYears(1);

        var confirmed = await env.Receipts.ConfirmAsync(
            (await DraftAsync(
                env, supplier,
                Line(stocked, 1, 60_000m, "LOT-A", expiry),
                Line(stocked, 2, 60_000m, "LOT-A", expiry),
                Line(stocked, 1, 60_000m, "LOT-A", expiry.AddMonths(1)),
                Line(stocked, 1, 60_000m, "LOT-B", expiry))).Id,
            Token);

        Assert.Equal(3, confirmed.Items.Select(i => i.InventoryLotId).Distinct().Count());
        var shared = confirmed.Items.Where(i => i.SupplierLotNumber == "LOT-A" && i.ExpiryDate == expiry).ToList();
        Assert.Equal(2, shared.Count);
        Assert.Single(shared.Select(i => i.InventoryLotId).Distinct());
        var lot = await env.Inventory.GetLotAsync(shared[0].InventoryLotId!.Value, Token);
        Assert.Equal(18, lot.QuantityOnHand);
        Assert.Equal(180_000m, lot.TotalCostValue);
        var movement = await env.Inventory.GetMovementAsync(confirmed.StockMovementId!.Value, Token);
        Assert.Equal(4, movement.Items.Count);
    }

    [RealDbFact]
    public async Task Products_without_lot_tracking_share_one_no_lot_bucket()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var supplier = await NewSupplierAsync(env);
        var stocked = await NewStockedProductAsync(env, lotTracked: false, expiryRequired: false);
        GoodsReceiptItemRequest Plain(long boxes, decimal cost, string? lot, DateOnly? expiry) =>
            new(stocked.StoreProductId, stocked.BoxId, boxes, cost, lot, null, expiry);

        var first = await env.Receipts.ConfirmAsync((await DraftAsync(env, supplier, Plain(1, 60_000m, null, null))).Id, Token);
        var second = await env.Receipts.ConfirmAsync(
            (await DraftAsync(env, supplier, Plain(2, 90_000m, "WHATEVER", Today.AddYears(1)))).Id, Token);

        var lotId = first.Items.Single().InventoryLotId!.Value;
        Assert.Equal(lotId, second.Items.Single().InventoryLotId);
        var lot = await env.Inventory.GetLotAsync(lotId, Token);
        Assert.Null(lot.LotNumber);
        Assert.Null(lot.ExpiryDate);
        Assert.Equal(18, lot.QuantityOnHand);
        Assert.Equal(240_000m, lot.TotalCostValue);
    }

    [RealDbFact]
    public async Task Lot_status_rules_on_receiving_depleted_is_reactivated_blocked_stays_blocked()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var supplier = await NewSupplierAsync(env);
        var stocked = await NewStockedProductAsync(env);
        var expiry = Today.AddYears(1);
        var first = await env.Receipts.ConfirmAsync((await DraftAsync(env, supplier, Line(stocked, 1, 60_000m, "LOT-A", expiry))).Id, Token);
        var lotId = first.Items.Single().InventoryLotId!.Value;

        var depleted = await env.Context.InventoryLots.SingleAsync(l => l.Id == lotId, Token);
        depleted.ChangeStatus(InventoryLotStatus.Depleted);
        await env.Context.SaveChangesAsync(Token);
        await env.Receipts.ConfirmAsync((await DraftAsync(env, supplier, Line(stocked, 1, 60_000m, "LOT-A", expiry))).Id, Token);
        Assert.Equal("ACTIVE", (await env.Inventory.GetLotAsync(lotId, Token)).Status);

        await env.Inventory.ChangeLotStatusAsync(lotId, new ChangeLotStatusRequest("BLOCKED"), Token);
        await env.Receipts.ConfirmAsync((await DraftAsync(env, supplier, Line(stocked, 1, 60_000m, "LOT-A", expiry))).Id, Token);

        var lot = await env.Inventory.GetLotAsync(lotId, Token);
        Assert.Equal("BLOCKED", lot.Status);
        Assert.Equal(18, lot.QuantityOnHand);
    }

    [RealDbFact]
    public async Task Confirmation_is_refused_for_empty_receipts_stale_lines_and_inactive_suppliers_and_changes_nothing()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var supplier = await NewSupplierAsync(env);
        var stocked = await NewStockedProductAsync(env);
        var lotsBefore = await env.Context.InventoryLots.CountAsync(Token);
        var movementsBefore = await env.Context.StockMovements.CountAsync(Token);

        var empty = await DraftAsync(env, supplier);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Receipts.ConfirmAsync(empty.Id, Token));

        var stale = await DraftAsync(env, supplier, Line(stocked, 1, 60_000m));
        await env.Products.ChangeStatusAsync(stocked.Product.Id, new ChangeProductStatusRequest("DISCONTINUED"), Token);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Receipts.ConfirmAsync(stale.Id, Token));
        Assert.StartsWith("Line 1:", error.Message);

        var other = await NewStockedProductAsync(env);
        var inactive = await DraftAsync(env, supplier, Line(other, 1, 60_000m));
        await env.Suppliers.SetActiveAsync(supplier.Id, false, Token);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Receipts.ConfirmAsync(inactive.Id, Token));

        await Assert.ThrowsAsync<NotFoundException>(() => env.Receipts.ConfirmAsync(Guid.NewGuid(), Token));
        Assert.Equal(lotsBefore, await env.Context.InventoryLots.CountAsync(Token));
        Assert.Equal(movementsBefore, await env.Context.StockMovements.CountAsync(Token));
        Assert.Equal("DRAFT", (await env.Receipts.GetAsync(stale.Id, Token)).Status);
    }

    [RealDbFact]
    public async Task Two_writers_of_the_same_lot_balance_conflict_on_the_version()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var supplier = await NewSupplierAsync(env);
        var stocked = await NewStockedProductAsync(env);
        var confirmed = await env.Receipts.ConfirmAsync((await DraftAsync(env, supplier, Line(stocked, 1, 60_000m))).Id, Token);
        var lotId = confirmed.Items.Single().InventoryLotId!.Value;

        await using var first = session.NewContext();
        await using var second = session.NewContext();
        var firstLot = await first.InventoryLots.Include(l => l.Balance).SingleAsync(l => l.Id == lotId, Token);
        var secondLot = await second.InventoryLots.Include(l => l.Balance).SingleAsync(l => l.Id == lotId, Token);

        firstLot.ReceiveStock(6, 10_000m);
        await first.SaveChangesAsync(Token);
        secondLot.ReceiveStock(6, 10_000m);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Token));
    }

    [RealDbFact]
    public async Task The_receipt_row_lock_can_be_taken_inside_the_transaction()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var receipt = await DraftAsync(env, await NewSupplierAsync(env));

        await env.Locks.LockGoodsReceiptAsync(receipt.Id, Token);
        await env.Locks.LockGoodsReceiptAsync(Guid.NewGuid(), Token); // a missing row is not an error here
    }

    // ----- Inventory queries -----

    [RealDbFact]
    public async Task Lot_queries_filter_by_stock_expiry_and_search_and_show_available_quantity()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var supplier = await NewSupplierAsync(env);
        var stocked = await NewStockedProductAsync(env);
        var soon = Today.AddMonths(2);
        var late = Today.AddYears(2);
        var confirmed = await env.Receipts.ConfirmAsync(
            (await DraftAsync(env, supplier, Line(stocked, 2, 60_000m, "SOON", soon), Line(stocked, 1, 60_000m, "LATE", late))).Id, Token);
        var soonLotId = confirmed.Items.First(i => i.SupplierLotNumber == "SOON").InventoryLotId!.Value;

        // Reserved quantity reduces availability but not the quantity on hand.
        var soonLot = await env.Context.InventoryLots.Include(l => l.Balance).SingleAsync(l => l.Id == soonLotId, Token);
        soonLot.Reserve(5, Today);
        await env.Context.SaveChangesAsync(Token);

        var all = await env.Inventory.ListLotsAsync(new InventoryLotListRequest { StoreProductId = stocked.StoreProductId }, Token);
        Assert.Equal(["SOON", "LATE"], all.Items.Select(l => l.LotNumber));
        var shown = all.Items[0];
        Assert.Equal(12, shown.QuantityOnHand);
        Assert.Equal(5, shown.QuantityReserved);
        Assert.Equal(7, shown.QuantityAvailable);
        Assert.Equal(stocked.Product.Sku, shown.Sku);

        var expiring = await env.Inventory.ListLotsAsync(
            new InventoryLotListRequest { StoreProductId = stocked.StoreProductId, ExpiringBefore = Today.AddMonths(6) }, Token);
        Assert.Equal("SOON", Assert.Single(expiring.Items).LotNumber);
        var searched = await env.Inventory.ListLotsAsync(new InventoryLotListRequest { Search = "late", StoreProductId = stocked.StoreProductId }, Token);
        Assert.Equal("LATE", Assert.Single(searched.Items).LotNumber);
        var paged = await env.Inventory.ListLotsAsync(
            new InventoryLotListRequest { StoreProductId = stocked.StoreProductId, PageSize = 1, Page = 2 }, Token);
        Assert.Equal(2, paged.TotalCount);
        Assert.Equal("LATE", Assert.Single(paged.Items).LotNumber);
        Assert.Equal(2, (await env.Inventory.ListLotsAsync(
            new InventoryLotListRequest { StoreProductId = stocked.StoreProductId, HasStock = true }, Token)).TotalCount);
        Assert.Equal(0, (await env.Inventory.ListLotsAsync(
            new InventoryLotListRequest { StoreProductId = stocked.StoreProductId, HasStock = false }, Token)).TotalCount);
        await Assert.ThrowsAsync<NotFoundException>(() => env.Inventory.GetLotAsync(Guid.NewGuid(), Token));
    }

    [RealDbFact]
    public async Task Lot_status_can_be_changed_but_an_expired_lot_cannot_be_activated()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var supplier = await NewSupplierAsync(env);
        var stocked = await NewStockedProductAsync(env);
        var confirmed = await env.Receipts.ConfirmAsync((await DraftAsync(env, supplier, Line(stocked, 1, 60_000m))).Id, Token);
        var lotId = confirmed.Items.Single().InventoryLotId!.Value;

        Assert.Equal("QUARANTINED", (await env.Inventory.ChangeLotStatusAsync(lotId, new ChangeLotStatusRequest("quarantined"), Token)).Status);
        Assert.Equal("ACTIVE", (await env.Inventory.ChangeLotStatusAsync(lotId, new ChangeLotStatusRequest("ACTIVE"), Token)).Status);

        var expired = new InventoryLot(stocked.StoreProductId, "OLD", null, Today.AddDays(-3));
        env.Context.InventoryLots.Add(expired);
        await env.Context.SaveChangesAsync(Token);
        var view = await env.Inventory.GetLotAsync(expired.Id, Token);
        Assert.True(view.IsExpired);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Inventory.ChangeLotStatusAsync(expired.Id, new ChangeLotStatusRequest("ACTIVE"), Token));
        Assert.Equal("EXPIRED", (await env.Inventory.ChangeLotStatusAsync(expired.Id, new ChangeLotStatusRequest("EXPIRED"), Token)).Status);
        await Assert.ThrowsAsync<NotFoundException>(() => env.Inventory.ChangeLotStatusAsync(Guid.NewGuid(), new ChangeLotStatusRequest("BLOCKED"), Token));
    }

    [RealDbFact]
    public async Task Receipt_list_filters_by_status_supplier_dates_and_text()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var supplier = await NewSupplierAsync(env);
        var other = await NewSupplierAsync(env);
        var stocked = await NewStockedProductAsync(env);
        var draft = await DraftAsync(env, supplier, Line(stocked, 1, 60_000m));
        var confirmed = await env.Receipts.ConfirmAsync((await DraftAsync(env, supplier, Line(stocked, 1, 60_000m))).Id, Token);
        await DraftAsync(env, other);

        var bySupplier = await env.Receipts.ListAsync(new GoodsReceiptListRequest { SupplierId = supplier.Id }, Token);
        var drafts = await env.Receipts.ListAsync(new GoodsReceiptListRequest { SupplierId = supplier.Id, Status = "draft" }, Token);
        var today = await env.Receipts.ListAsync(
            new GoodsReceiptListRequest { SupplierId = supplier.Id, FromDate = Today, ToDate = Today }, Token);
        var past = await env.Receipts.ListAsync(
            new GoodsReceiptListRequest { SupplierId = supplier.Id, ToDate = Today.AddDays(-2) }, Token);
        var byNumber = await env.Receipts.ListAsync(new GoodsReceiptListRequest { Search = confirmed.ReceiptNumber.ToLowerInvariant() }, Token);

        Assert.Equal(2, bySupplier.TotalCount);
        Assert.Equal(draft.Id, Assert.Single(drafts.Items).Id);
        Assert.Equal(2, today.TotalCount);
        Assert.Equal(0, past.TotalCount);
        Assert.Equal(confirmed.Id, Assert.Single(byNumber.Items).Id);
        Assert.Equal(1, bySupplier.Items.Count(i => i.Status == "CONFIRMED"));
        Assert.All(bySupplier.Items, i => Assert.Equal(supplier.Name, i.SupplierName));
        Assert.Matches(new Regex(@"^GR-\d{8}-\d{4}$"), bySupplier.Items[0].ReceiptNumber);
    }

    // ----- Excel import (F4.3) -----

    // A filled receipt sheet: columns in template order (SKU, Packaging, Quantity, UnitCost, LotNumber, ExpiryDate,
    // ManufactureDate); C# values become real Excel cells (numbers, dates, text).
    private static ReceiptImportFile Sheet(params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(ClosedXmlReceiptSpreadsheet.ReceiptSheet);
        for (var column = 0; column < ReceiptSheetColumns.All.Count; column++)
        {
            sheet.Cell(1, column + 1).Value = ReceiptSheetColumns.All[column];
        }

        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < rows[row].Length; column++)
            {
                sheet.Cell(row + 2, column + 1).Value = XLCellValue.FromObject(rows[row][column]);
            }
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        return new ReceiptImportFile(stream, @"C:\fakepath\nhap-kho.xlsx", stream.Length);
    }

    private static DateTime Date(DateOnly day) => day.ToDateTime(TimeOnly.MinValue);

    private static async Task<int> ReceiptCountAsync(Env env, Guid supplierId) =>
        await env.Context.GoodsReceipts.CountAsync(r => r.SupplierId == supplierId, Token);

    [RealDbFact]
    public async Task The_import_template_lists_only_the_purchasable_packagings_of_store_products()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var stocked = await NewStockedProductAsync(env);

        var template = await env.Import.GetTemplateAsync(Token);

        Assert.Equal(ReceiptImportRules.ContentType, template.ContentType);
        Assert.Equal(ReceiptImportRules.TemplateFileName, template.FileName);
        using var workbook = new XLWorkbook(new MemoryStream(template.Content));
        var products = workbook.Worksheet(ClosedXmlReceiptSpreadsheet.ProductsSheet);
        // Unit names come from the database (the seeded BOX / BOTTLE units may carry Vietnamese names).
        string UnitName(Guid id) => env.Context.Units.Where(u => u.Id == id).Select(u => u.Name).Single();
        var row = Assert.Single(products.RowsUsed(), r => r.Cell(1).GetString() == stocked.Product.Sku);
        Assert.Equal(UnitName(env.BoxUnit), row.Cell(3).GetString());
        Assert.Equal(6, row.Cell(5).GetValue<int>());
        Assert.Equal(UnitName(env.BottleUnit), row.Cell(6).GetString());
        Assert.Equal("YES", row.Cell(7).GetString());
        Assert.Equal("YES", row.Cell(8).GetString());
    }

    [RealDbFact]
    public async Task Preview_checks_every_row_like_manual_entry_and_saves_nothing()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var supplier = await NewSupplierAsync(env);
        var stocked = await NewStockedProductAsync(env);
        var sku = stocked.Product.Sku;
        var nextYear = Date(Today.AddYears(1));

        var preview = await env.Import.PreviewAsync(
            new ReceiptImportRequest(supplier.Id),
            Sheet(
                [sku, "box", 2, 120_000, "LOT-A", nextYear],                  // row 2: valid
                ["NOPE-" + Tag(), "Box", 1, 1_000, "L", nextYear],            // row 3: unknown SKU
                [sku, "Bottle", 1, 1_000, "L", nextYear],                     // row 4: base unit, not a purchase unit
                [sku, "Box", 1, 1_000, null, nextYear],                       // row 5: lot required
                [sku, "Box", 1, 1_000, "L", Date(Today.AddDays(-1))],         // row 6: expired
                [sku, "Box", 0, 1.234, "L", "2027/01/31"]),                   // row 7: quantity, cost, date format
            Token);

        Assert.Equal("nhap-kho.xlsx", preview.FileName);
        Assert.Equal((6, 1, 240_000m), (preview.RowCount, preview.ValidRowCount, preview.SubtotalAmount));
        var valid = preview.Rows[0];
        Assert.Equal((2, stocked.StoreProductId, stocked.BoxId, 2L, 120_000m, 240_000m),
            (valid.RowNumber, valid.StoreProductId!.Value, valid.ProductPackagingId!.Value, valid.Quantity!.Value,
                valid.UnitCost!.Value, valid.LineTotalAmount!.Value));
        Assert.Empty(valid.Errors);

        string[] Columns(int index) => preview.Rows[index].Errors.Select(e => e.Column).ToArray();
        Assert.Equal(["SKU"], Columns(1));
        Assert.Equal(["Packaging"], Columns(2));
        Assert.Contains("purchase unit", preview.Rows[2].Errors[0].Message);
        Assert.Equal(["LotNumber"], Columns(3));
        Assert.Equal(["ExpiryDate"], Columns(4));
        Assert.Equal(["Quantity", "UnitCost", "ExpiryDate"], Columns(5));
        Assert.Equal(0, await ReceiptCountAsync(env, supplier.Id));
    }

    [RealDbFact]
    public async Task Import_creates_one_excel_draft_that_confirms_like_a_manual_one()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var supplier = await NewSupplierAsync(env);
        var stocked = await NewStockedProductAsync(env);
        var storeSku = $"KHO-{Tag()}";
        await env.StoreProducts.UpdateAsync(stocked.StoreProductId, new UpdateStoreProductRequest(storeSku, null), Token);

        var draft = await env.Import.ImportAsync(
            new ReceiptImportRequest(supplier.Id, SupplierInvoiceNumber: "HD-001"),
            Sheet(
                [storeSku.ToLowerInvariant(), "BOX", 2, 120_000, "LOT-A", Date(Today.AddYears(1)), Date(Today.AddMonths(-1))],
                [stocked.Product.Sku, "Box", 1, 60_000.5, "lot-a ", $"{Today.AddYears(1):dd/MM/yyyy}"]),
            Token);

        Assert.Equal(("DRAFT", "EXCEL_TEMPLATE", "HD-001"), (draft.Status, draft.SourceType, draft.SupplierInvoiceNumber));
        Assert.Equal(2, draft.Items.Count);
        Assert.Equal(300_000.5m, draft.TotalAmount);
        Assert.Equal(
            "nhap-kho.xlsx",
            await env.Context.GoodsReceipts.Where(r => r.Id == draft.Id).Select(r => r.SourceFileName).SingleAsync(Token));

        var confirmed = await env.Receipts.ConfirmAsync(draft.Id, Token);

        // Same logical lot (lot number ignoring case/spaces + same expiry) → one lot of 18 bottles.
        Assert.Equal("CONFIRMED", confirmed.Status);
        Assert.Single(confirmed.Items.Select(i => i.InventoryLotId).Distinct());
    }

    [RealDbFact]
    public async Task Import_with_one_invalid_row_saves_nothing_and_names_the_row()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var supplier = await NewSupplierAsync(env);
        var stocked = await NewStockedProductAsync(env);
        var file = Sheet(
            [stocked.Product.Sku, "Box", 1, 10_000, "LOT-A", Date(Today.AddYears(1))],
            [stocked.Product.Sku, "Box", 1, 10_000, null, Date(Today.AddYears(1))]);

        var rejected = await Assert.ThrowsAsync<BusinessRuleException>(
            () => env.Import.ImportAsync(new ReceiptImportRequest(supplier.Id), file, Token));

        Assert.Contains("1 of 2 rows", rejected.Message);
        var error = Assert.Single(Assert.Single(rejected.Errors!, pair => pair.Key == "row 3").Value);
        Assert.StartsWith("LotNumber:", error);
        Assert.Equal(0, await ReceiptCountAsync(env, supplier.Id));

        await env.Suppliers.SetActiveAsync(supplier.Id, false, Token);
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => env.Import.PreviewAsync(new ReceiptImportRequest(supplier.Id), Sheet([stocked.Product.Sku, "Box", 1, 1, "L"]), Token));
    }
}
