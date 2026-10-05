using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Inventory;
using AgriSage.Application.Features.Stocktakes;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Inventory;

// All fixtures and writes are rolled back by the outer RealDb session. Each endpoint call gets a fresh
// context, matching request scopes so tracked entities cannot hide changes made between requests.
public class StocktakeAdjustmentDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class MutableClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        public void Advance(int minutes = 1) => UtcNow = UtcNow.AddMinutes(minutes);
    }

    private sealed record Env(RealDb.Session Session, Guid StoreId, Guid ProductId, Guid CounterId, Guid ManagerId,
        Guid[] LotIds, MutableClock Clock, string Tag)
    {
        public async Task<T> Stocktake<T>(Func<StocktakeService, Task<T>> call, bool manager = false)
        {
            Session.CurrentUser.UserId = manager ? ManagerId : CounterId;
            Session.CurrentUser.Role = manager ? "STORE_OWNER" : "SALES_STAFF";
            await using var db = Session.NewContext();
            var service = new StocktakeService(db, new RowLockService(db), Session.CurrentUser, Clock,
                new AuditTrail(db, Session.CurrentUser, Clock), new StocktakeQueries(db), new StockAdjustmentPosting(db, Clock));
            return await call(service);
        }

        public async Task Delete(Guid id)
        {
            await Stocktake(async service => { await service.DeleteAsync(id, Token); return true; });
        }

        public async Task<StockMovementResponse> Adjust(params StockAdjustmentLine[] lines)
        {
            Session.CurrentUser.UserId = ManagerId;
            Session.CurrentUser.Role = "STORE_OWNER";
            await using var db = Session.NewContext();
            var locks = new RowLockService(db);
            var service = new StockAdjustmentService(db, locks, Session.CurrentUser, new StockAdjustmentPosting(db, Clock),
                new InventoryService(db, Clock, locks));
            return await service.CreateAsync(new("OTHER", "Rollback-only F4.2 test", lines), Token);
        }

        public Task<StocktakeResponse> Create(bool empty = false) => Stocktake(s => s.CreateAsync(new([ProductId], empty, Tag), Token));
        public Task<StocktakeResponse> Start(Guid id) => Stocktake(s => s.StartAsync(id, Token));
        public Task<StocktakeResponse> Count(Guid id, params StocktakeCountRequest[] counts) => Stocktake(s => s.CountAsync(id, new(counts), Token));
        public Task<StocktakeResponse> Complete(Guid id, bool manager = true) => Stocktake(s => s.CompleteAsync(id, Token), manager);
        public Task<StocktakeResponse> Get(Guid id, StocktakeDetailRequest? request = null) => Stocktake(s => s.GetAsync(id, request ?? new(), Token));
    }

    private static async Task<Env> Prepare(RealDb.Session session, long reserved = 0)
    {
        await using var db = session.NewContext();
        var tag = Guid.NewGuid().ToString("N")[..10];
        var clock = new MutableClock();
        async Task<User> Actor(RoleCode code)
        {
            var role = await db.Roles.IgnoreQueryFilters().SingleOrDefaultAsync(r => r.Code == code, Token);
            if (role is null) { role = new Role(code, code.ToString()); db.Roles.Add(role); }
            var user = new User(role.Id, "Stocktake tester", "hash", $"{tag}-{code}@example.test", null);
            db.Users.Add(user);
            return user;
        }
        var counter = await Actor(RoleCode.SalesStaff);
        var manager = await Actor(RoleCode.StoreOwner);
        var store = await db.Stores.SingleOrDefaultAsync(s => s.Status == StoreStatus.Active, Token);
        if (store is null) { store = new Store($"S-{tag}", "Test store", "Test address", "Test"); db.Stores.Add(store); }
        var category = new Category($"C-{tag}", "Test category");
        var unit = new Unit($"U-{tag}", "Test unit");
        var product = new Product(category.Id, $"P-{tag}", "Stocktake test product");
        product.AddPackaging(unit.Id, 1, true, true, true, "ACTIVE");
        var sp = new StoreProduct(store.Id, product.Id);
        db.AddRange(category, unit, product, sp);
        var lots = new[] { new InventoryLot(sp.Id, "A"), new InventoryLot(sp.Id, "B"), new InventoryLot(sp.Id, "EMPTY") };
        lots[0].ReceiveStock(10, 100m);
        lots[1].ReceiveStock(10, 200m);
        if (reserved > 0) lots[0].Reserve(reserved, BusinessCalendar.Today(clock.UtcNow));
        db.InventoryLots.AddRange(lots);
        await db.SaveChangesAsync(Token);
        return new(session, store.Id, sp.Id, counter.Id, manager.Id, lots.Select(l => l.Id).ToArray(), clock, tag);
    }

    [RealDbFact]
    public async Task Manual_adjustments_post_signed_movements_with_average_cost_and_cost_for_empty_lots()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await Prepare(session);
        var added = await env.Adjust(new StockAdjustmentLine(env.LotIds[0], 5, 200m));
        Assert.Equal("ADJUSTMENT_IN", added.MovementType);
        Assert.Equal("POSTED", added.Status);
        Assert.Equal(15, Assert.Single(added.Items).QuantityOnHandAfter);
        Assert.Equal(2000m, added.Items[0].TotalCostValueAfter);
        env.Clock.Advance();
        var removed = await env.Adjust(new StockAdjustmentLine(env.LotIds[0], -4, 1m));
        Assert.Equal("ADJUSTMENT_OUT", removed.MovementType);
        Assert.Equal(-4, Assert.Single(removed.Items).QuantityDeltaBase);
        Assert.Equal(133.333333m, removed.Items[0].UnitCostSnapshot);
        Assert.Equal(1466.666668m, removed.Items[0].TotalCostValueAfter);
        var defaultCost = await env.Adjust(new StockAdjustmentLine(env.LotIds[1], 2));
        Assert.Equal(200m, Assert.Single(defaultCost.Items).UnitCostSnapshot);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Adjust(new StockAdjustmentLine(env.LotIds[2], 2)));
        var empty = await env.Adjust(new StockAdjustmentLine(env.LotIds[2], 2, 12.345678m));
        Assert.Equal(24.691356m, Assert.Single(empty.Items).TotalCostValueAfter);
        var emptied = await env.Adjust(new StockAdjustmentLine(env.LotIds[0], -11));
        Assert.Equal(0, Assert.Single(emptied.Items).QuantityOnHandAfter);
        Assert.Equal(0, emptied.Items[0].TotalCostValueAfter);
    }

    [RealDbFact]
    public async Task Manual_adjustments_prevalidate_all_lots_and_never_consume_reserved_stock()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await Prepare(session, reserved: 8);
        await using var db = session.NewContext();
        var movementCount = await db.StockMovements.CountAsync(Token);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Adjust(new StockAdjustmentLine(env.LotIds[1], -1), new StockAdjustmentLine(env.LotIds[0], -3)));
        Assert.Contains(env.LotIds[0].ToString(), error.Errors!.Keys);
        Assert.Equal(movementCount, await db.StockMovements.CountAsync(Token));
        Assert.Equal(10, await db.InventoryLotBalances.Where(b => b.InventoryLotId == env.LotIds[1]).Select(b => b.QuantityOnHand).SingleAsync(Token));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Adjust(new StockAdjustmentLine(Guid.NewGuid(), 1, 100m)));
        var valid = await env.Adjust(new StockAdjustmentLine(env.LotIds[0], -2));
        Assert.Equal(8, Assert.Single(valid.Items).QuantityOnHandAfter);
    }

    [RealDbFact]
    public async Task Stocktake_lifecycle_creates_two_linked_movements_and_queries_filtered_items_and_lists()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await Prepare(session);
        var draft = await env.Create();
        Assert.Equal("DRAFT", draft.Status);
        Assert.Equal(2, draft.Items.Count);
        Assert.StartsWith($"ST-{BusinessCalendar.Today(env.Clock.UtcNow):yyyyMMdd}-", draft.StocktakeNumber);
        Assert.All(draft.Items, i => Assert.Equal(env.Clock.UtcNow, i.SnapshotAt));
        await env.Start(draft.Id);
        env.Clock.Advance();
        var count = await env.Count(draft.Id, new StocktakeCountRequest(draft.Items[0].Id, 12, ReasonCode: "OTHER"));
        Assert.Equal(1, count.Totals.Counted);
        var uncounted = await env.Get(draft.Id, new(OnlyUncounted: true));
        Assert.Single(uncounted.Items);
        Assert.Equal(2, uncounted.Totals.Lines);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Complete(draft.Id));
        Assert.NotEmpty(error.Errors!);
        await env.Count(draft.Id, new StocktakeCountRequest(draft.Items[1].Id, 7, ReasonCode: "lost"));
        var differences = await env.Get(draft.Id, new(OnlyDifferences: true));
        Assert.Equal(2, differences.Items.Count);
        env.Clock.Advance();
        var complete = await env.Complete(draft.Id);
        Assert.Equal("COMPLETED", complete.Status);
        Assert.Equal(env.ManagerId, complete.CompletedBy);
        Assert.Equal(2, complete.Movements.Count);
        Assert.Contains(complete.Movements, m => m.MovementType == "ADJUSTMENT_IN");
        Assert.Contains(complete.Movements, m => m.MovementType == "ADJUSTMENT_OUT");
        await using var db = session.NewContext();
        var movements = await db.StockMovements.Include(m => m.Items).Where(m => m.StocktakeId == draft.Id).OrderBy(m => m.MovementNumber).ToListAsync(Token);
        Assert.All(movements, m => Assert.Equal(StockMovementStatus.Posted, m.Status));
        Assert.Equal(2, movements.Sum(m => m.Items.Count));
        Assert.Equal(DocumentNumbers.SequenceOf(movements[0].MovementNumber, "SM", BusinessCalendar.Today(env.Clock.UtcNow)) + 1,
            DocumentNumbers.SequenceOf(movements[1].MovementNumber, "SM", BusinessCalendar.Today(env.Clock.UtcNow)));
        Assert.Equal(12, await db.InventoryLotBalances.Where(b => b.InventoryLotId == env.LotIds[0]).Select(b => b.QuantityOnHand).SingleAsync(Token));
        Assert.Equal(7, await db.InventoryLotBalances.Where(b => b.InventoryLotId == env.LotIds[1]).Select(b => b.QuantityOnHand).SingleAsync(Token));
        var list = await env.Stocktake(s => s.ListAsync(new()
        {
            Status = "completed",
            Search = env.Tag,
            PageSize = 1,
            FromDate = BusinessCalendar.Today(DateTimeOffset.UtcNow),
            ToDate = BusinessCalendar.Today(DateTimeOffset.UtcNow)
        }, Token));
        Assert.Equal(draft.Id, Assert.Single(list.Items).Id);
        Assert.Equal(1, list.TotalCount);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Complete(draft.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Count(draft.Id, new StocktakeCountRequest(draft.Items[0].Id, 12)));
    }

    [RealDbFact]
    public async Task Stale_lines_detect_offsetting_movements_then_refresh_only_those_lines_and_require_recount()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await Prepare(session);
        var draft = await env.Create();
        await env.Start(draft.Id);
        env.Clock.Advance();
        await env.Adjust(new StockAdjustmentLine(env.LotIds[0], -1));
        await env.Adjust(new StockAdjustmentLine(env.LotIds[0], 1));
        env.Clock.Advance();
        await env.Count(draft.Id, draft.Items.Select(i => new StocktakeCountRequest(i.Id, 10)).ToArray());
        var stale = await env.Get(draft.Id);
        Assert.True(Assert.Single(stale.Items, i => i.InventoryLotId == env.LotIds[0]).IsStale);
        Assert.False(Assert.Single(stale.Items, i => i.InventoryLotId == env.LotIds[1]).IsStale);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Complete(draft.Id));
        Assert.Contains(env.LotIds[0].ToString(), error.Errors!.Keys);
        // Allow the contract's five-minute lookback margin to pass before taking the new snapshot.
        env.Clock.Advance(6);
        var refreshed = await env.Stocktake(s => s.RefreshStaleAsync(draft.Id, Token));
        var changed = Assert.Single(refreshed.Items, i => i.InventoryLotId == env.LotIds[0]);
        var unchanged = Assert.Single(refreshed.Items, i => i.InventoryLotId == env.LotIds[1]);
        Assert.Null(changed.CountedQuantity);
        Assert.Null(changed.CountedAt);
        Assert.Equal(env.Clock.UtcNow, changed.SnapshotAt);
        Assert.Equal(10, unchanged.CountedQuantity);
        Assert.Equal(draft.Items[1].SnapshotAt, unchanged.SnapshotAt);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Complete(draft.Id));
        env.Clock.Advance();
        await env.Count(draft.Id, new StocktakeCountRequest(changed.Id, 10));
        env.Clock.Advance();
        var complete = await env.Complete(draft.Id);
        Assert.Equal("COMPLETED", complete.Status);
        Assert.Empty(complete.Movements);
    }

    [RealDbFact]
    public async Task Movements_after_count_do_not_stale_the_line_and_difference_applies_to_current_stock()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await Prepare(session);
        var draft = await env.Create();
        await env.Start(draft.Id);
        env.Clock.Advance();
        await env.Count(draft.Id, new StocktakeCountRequest(draft.Items[0].Id, 8, ReasonCode: "OTHER"), new StocktakeCountRequest(draft.Items[1].Id, 10));
        env.Clock.Advance();
        await env.Adjust(new StockAdjustmentLine(env.LotIds[0], -1));
        Assert.All((await env.Get(draft.Id)).Items, i => Assert.False(i.IsStale));
        env.Clock.Advance();
        var complete = await env.Complete(draft.Id);
        Assert.Equal("ADJUSTMENT_OUT", Assert.Single(complete.Movements).MovementType);
        await using var db = session.NewContext();
        Assert.Equal(7, await db.InventoryLotBalances.Where(b => b.InventoryLotId == env.LotIds[0]).Select(b => b.QuantityOnHand).SingleAsync(Token));
    }

    [RealDbFact]
    public async Task Empty_lots_need_entered_cost_and_counting_actor_cannot_approve()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await Prepare(session);
        var draft = await env.Create(empty: true);
        Assert.Equal(3, draft.Items.Count);
        await env.Start(draft.Id);
        env.Clock.Advance();
        var empty = Assert.Single(draft.Items, i => i.InventoryLotId == env.LotIds[2]);
        await env.Count(draft.Id, draft.Items.Select(i => new StocktakeCountRequest(i.Id,
            i.InventoryLotId == env.LotIds[2] ? 3 : 10, ReasonCode: "OTHER")).ToArray());
        var missingCost = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Complete(draft.Id));
        Assert.Contains(env.LotIds[2].ToString(), missingCost.Errors!.Keys);
        await env.Count(draft.Id, new StocktakeCountRequest(empty.Id, 3, 25m, "OTHER"));
        // A Store Owner may count, but must use a different actor to approve.
        await env.Stocktake(s => s.CountAsync(draft.Id, new([new(empty.Id, 3, 25m, "OTHER")]), Token), manager: true);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Complete(draft.Id));
        await env.Count(draft.Id, new StocktakeCountRequest(empty.Id, 3, 25m, "OTHER"));
        env.Clock.Advance();
        var complete = await env.Complete(draft.Id);
        Assert.Equal(75m, complete.Totals.DifferenceCostValue);
        await using var db = session.NewContext();
        Assert.Equal(75m, await db.InventoryLotBalances.Where(b => b.InventoryLotId == env.LotIds[2]).Select(b => b.TotalCostValue).SingleAsync(Token));
    }

    [RealDbFact]
    public async Task Stocktake_decrease_below_reserved_fails_without_posting_any_part_of_the_adjustments()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await Prepare(session, reserved: 8);
        var draft = await env.Create();
        await env.Start(draft.Id);
        env.Clock.Advance();
        await env.Count(draft.Id, new StocktakeCountRequest(draft.Items[0].Id, 7, ReasonCode: "OTHER"), new StocktakeCountRequest(draft.Items[1].Id, 11, ReasonCode: "OTHER"));
        env.Clock.Advance();
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Complete(draft.Id));
        await using var db = session.NewContext();
        Assert.Empty(await db.StockMovements.Where(m => m.StocktakeId == draft.Id).ToListAsync(Token));
        Assert.Equal(StocktakeStatus.InProgress, await db.Stocktakes.Where(s => s.Id == draft.Id).Select(s => s.Status).SingleAsync(Token));
        Assert.All(await db.InventoryLotBalances.Where(b => env.LotIds.Take(2).Contains(b.InventoryLotId)).Select(b => b.QuantityOnHand).ToListAsync(Token), q => Assert.Equal(10, q));
    }

    [RealDbFact]
    public async Task Draft_delete_cancel_and_missing_reason_rules_preserve_history_and_do_not_change_stock()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await Prepare(session);
        var deleted = await env.Create();
        await env.Delete(deleted.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => env.Get(deleted.Id));
        await using var db = session.NewContext();
        Assert.NotNull(await db.Stocktakes.IgnoreQueryFilters().Where(s => s.Id == deleted.Id).Select(s => s.DeletedAt).SingleAsync(Token));
        var draft = await env.Create();
        Assert.NotEqual(deleted.StocktakeNumber, draft.StocktakeNumber);
        await env.Start(draft.Id);
        await Assert.ThrowsAsync<AgriSage.Domain.Common.Exceptions.DomainException>(() => env.Delete(draft.Id));
        env.Clock.Advance();
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Count(draft.Id, new StocktakeCountRequest(draft.Items[0].Id, 9)));
        Assert.All((await env.Get(draft.Id)).Items, i => Assert.Null(i.CountedQuantity));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Count(draft.Id, new StocktakeCountRequest(Guid.NewGuid(), 10)));
        var cancelled = await env.Stocktake(s => s.CancelAsync(draft.Id, new("Test cancellation"), Token));
        Assert.Equal("CANCELLED", cancelled.Status);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityId == draft.Id && a.Action == "STOCKTAKE_CANCELLED" && a.Reason == "Test cancellation", Token));
        await Assert.ThrowsAsync<AgriSage.Domain.Common.Exceptions.DomainException>(() => env.Start(draft.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Stocktake(s => s.CreateAsync(new([Guid.NewGuid()]), Token)));
    }
}
