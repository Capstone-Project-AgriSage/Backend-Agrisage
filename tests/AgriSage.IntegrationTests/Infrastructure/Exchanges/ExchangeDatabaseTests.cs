using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Credit;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Exchanges;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Permissions;
using AgriSage.Application.Features.Pricing;
using AgriSage.Application.Features.Returns;
using AgriSage.Application.Features.Reports;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;
using AgriSage.Domain.Features.Pricing.Entities;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Infrastructure.Persistence;
using AgriSage.IntegrationTests.Infrastructure.Payments;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AgriSage.IntegrationTests.Infrastructure.Exchanges;

// Applies all reviewed migrations in a transaction-local schema; the schema and every fixture roll back.
// The public schema is inspected read-only and never migrated or modified by this test.
public class ExchangeDatabaseTests(ITestOutputHelper output)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Clock : IDateTimeProvider { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private static (ExchangeService Exchange, SalesReturnService Returns) Services(AgriSageDbContext db, RealDb.MutableUser user)
    {
        var clock = new Clock(); var locks = new RowLockService(db); var audit = new AuditTrail(db, user, clock);
        var sources = new ReturnSources(db); var queries = new SalesReturnQueries(db, sources);
        var permissions = new PermissionEvaluator(db, user);
        var returns = new SalesReturnService(db, locks, user, clock, audit, queries, sources, new DebtReturnPosting(db, locks, clock, audit), permissions);
        var ledger = new OrderPrepaymentLedger(db); var eligibility = new CreditEligibilityService(db, clock);
        return (new ExchangeService(db, locks, user, clock, audit, queries, returns,
            new OrderBuilder(db, new PriceResolver(db), user, clock, audit, eligibility),
            new OrderConfirmer(db, locks, new OrderSettlementGuard(db, locks, ledger, eligibility, clock), user, clock, audit),
            new FulfillmentPostingService(db, locks, new FulfillmentFinancialPosting(db, locks, ledger, audit),
                new OrderPaymentCancellation(db, locks, new FakePaymentGateway(), clock, audit)),
            new PaymentAllocator(clock, new CreditReservationAdjuster(db, locks, clock), new DebtRepaymentPosting(db, locks, clock)), permissions), returns);
    }
    [RealDbFact]
    public async Task Isolated_postgres_verifies_cash_equal_refund_debt_restock_writeoff_ownership_retry_and_failure_atomicity()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var setup = session.NewContext();
        var applied = await setup.Database.GetAppliedMigrationsAsync(Token);
        output.WriteLine("Configured database applied migrations: " + string.Join(", ", applied));
        var schema = "exchange_test_" + Guid.NewGuid().ToString("N");
        var schemaSql = "CREATE SCHEMA " + schema + "; SET LOCAL search_path TO " + schema + ";";
        await setup.Database.ExecuteSqlRawAsync(schemaSql, Token);
        var script = setup.GetService<IMigrator>().GenerateScript(null, null, MigrationsSqlGenerationOptions.NoTransactions);
        await setup.Database.ExecuteSqlRawAsync(script, Token);
        var (customer, store, farmer) = await session.SeedAsync(setup);
        var adminRole = new Role(RoleCode.Admin, "Admin"); var admin = new User(adminRole.Id, "Exchange tester", "hash", "exchange-admin@example.test", null);
        setup.AddRange(adminRole, admin);
        foreach (var definition in PermissionCatalog.Entries.Where(d => d.DefaultRoles.Contains("FARMER")))
            setup.RolePermissions.Add(new RolePermission(customer.RoleId, definition.Id, true));
        var prices = new PriceList(store.Id, "EX-PRICE", "Exchange price", DateTimeOffset.UtcNow.AddDays(-1), isWalkInDefault: true); prices.Activate();
        var category = new Category("EX", "Exchange"); var unit = new Unit("BOTTLE", "Bottle");
        setup.AddRange(prices, category, unit); await setup.SaveChangesAsync(Token);
        session.CurrentUser.UserId = admin.Id; session.CurrentUser.Role = "ADMIN";
        async Task<T> Run<T>(Func<ExchangeService, Task<T>> call)
        { await using var db = session.NewContext(); return await call(Services(db, session.CurrentUser).Exchange); }
        async Task<ExchangeResponse> Detail(Guid id) => await Run(s => s.GetAsync(id, false, Token));
        var scenario = 0;
        foreach (var (price, unpaid, damaged) in new[] { (250m, 0m, false), (200m, 0m, false), (150m, 0m, true), (100m, 150m, false) })
        {
            scenario++;
            var oldProduct = new Product(category.Id, "EX-OLD-" + scenario, "Old goods " + scenario);
            var oldPack = oldProduct.AddPackaging(unit.Id, 1, true, true, true, "ACTIVE");
            var oldSp = new StoreProduct(store.Id, oldProduct.Id); var oldLot = new InventoryLot(oldSp.Id, "OLD-" + scenario);
            var newProduct = new Product(category.Id, "EX-NEW-" + scenario, "Replacement " + scenario);
            var newPack = newProduct.AddPackaging(unit.Id, 1, true, true, true, "ACTIVE");
            var newSp = new StoreProduct(store.Id, newProduct.Id); var newLot = new InventoryLot(newSp.Id, "NEW-" + scenario);
            var order = new Order(store.Id, "OD-EX-" + scenario, OrderSource.Counter, CustomerType.Registered, admin.Id, "Customer",
                unpaid > 0 ? SettlementType.Credit : SettlementType.FullPayment, FulfillmentType.Pickup, farmer.Id);
            var item = order.AddItem(oldSp, oldPack, oldProduct.Sku, oldProduct.Name, "Bottle", 2, 100m);
            order.Confirm(admin.Id, DateTimeOffset.UtcNow, unpaid > 0 ? 30 : null);
            var stockIn = new StockMovement(store.Id, "SM-EX-IN-" + scenario, StockMovementType.StockIn, DateTimeOffset.UtcNow, admin.Id);
            stockIn.AddItem(oldLot.Id, oldLot.ReceiveStock(100, 10m)); stockIn.AddItem(newLot.Id, newLot.ReceiveStock(10, 20m)); stockIn.Post(admin.Id, DateTimeOffset.UtcNow);
            var sale = new StockMovement(store.Id, "SM-EX-SALE-" + scenario, StockMovementType.Sale, DateTimeOffset.UtcNow, admin.Id, orderId: order.Id);
            var saleLine = sale.AddItem(oldLot.Id, oldLot.IssueUnreserved(2)); sale.Post(admin.Id, DateTimeOffset.UtcNow);
            order.RecordFulfillment(item.Id, 2, admin.Id, DateTimeOffset.UtcNow);
            setup.AddRange(oldProduct, oldSp, oldLot, newProduct, newSp, newLot, order, stockIn, sale, new PriceListItem(prices.Id, newSp, newPack, price));
            var paid = new Payment(store.Id, "PM-EX-ORIGINAL-" + scenario, PaymentContext.OrderPayment, PaymentMethod.Cash, 200m - unpaid,
                DateTimeOffset.UtcNow, farmer.Id, admin.Id, orderId: order.Id);
            paid.MarkPaid(PaymentConfirmationSource.Staff, DateTimeOffset.UtcNow, admin.Id);
            var allocation = paid.AllocateToOrder(order.Id, paid.Amount, DateTimeOffset.UtcNow, admin.Id); paid.ConsumePrepayment(allocation.Id, paid.Amount); setup.Add(paid);
            DebtAccount? account = null;
            if (unpaid > 0)
            {
                account = new DebtAccount(store.Id, farmer.Id);
                var entry = account.CreateCreditSaleEntry("DE-EX", DebtEntrySourceType.Pickup, order.Id, sale.Id, 200m, 200m - unpaid,
                    DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30), admin.Id, DateTimeOffset.UtcNow);
                setup.AddRange(account, entry.Entry, entry.Transaction);
            }
            await setup.SaveChangesAsync(Token);
            var request = new CreateExchangeRequest(order.Id, Guid.NewGuid(), "Exchange reason",
                [new(item.Id, 1, "OTHER", OriginalStockMovementItemId: saleLine.Id), new(item.Id, 1, "OTHER", OriginalStockMovementItemId: saleLine.Id)],
                [new(newSp.Id, newPack.Id, 1)]);
            if (scenario == 1)
            {
                var rejected = await Run(s => s.CreateAsync(request with { RequestKey = Guid.NewGuid() }, false, Token));
                await Run(s => s.RejectAsync(rejected.Id, new("Wrong selection"), Token));
                session.CurrentUser.UserId = customer.Id; session.CurrentUser.Role = "FARMER";
                var cancelled = await Run(s => s.CreateAsync(request with { RequestKey = Guid.NewGuid() }, true, Token));
                await Run(s => s.CancelAsync(cancelled.Id, new("Choose again"), true, Token));
                var farmerCreated = await Run(s => s.CreateAsync(request with { RequestKey = Guid.NewGuid() }, true, Token));
                await Run(s => s.CancelAsync(farmerCreated.Id, new("Change selection"), true, Token));
                session.CurrentUser.UserId = admin.Id; session.CurrentUser.Role = "ADMIN";
            }
            var created = await Run(s => s.CreateAsync(request, false, Token));
            Assert.Equal(created.Id, (await Run(s => s.CreateAsync(request, false, Token))).Id);
            await Assert.ThrowsAsync<ConflictException>(() => Run(s => s.CreateAsync(request with { Reason = "Changed" }, false, Token)));
            await Assert.ThrowsAsync<BusinessRuleException>(() => Run(s => s.CreateAsync(request with { RequestKey = Guid.NewGuid() }, false, Token)));
            await using (var standalone = session.NewContext())
                await Assert.ThrowsAsync<BusinessRuleException>(() => Services(standalone, session.CurrentUser).Returns.ApproveAsync(created.Return.Id, Token));
            session.CurrentUser.UserId = customer.Id; session.CurrentUser.Role = "FARMER";
            var own = await Run(s => s.GetAsync(created.Id, true, Token));
            Assert.All(own.Return.Items, i => Assert.Null(i.OriginalCogsUnitCost)); Assert.All(own.History, h => Assert.Null(h.ActorId));
            // A different authenticated Farmer profile cannot see this exchange.
            var other = new User(customer.RoleId, "Other", "hash", "other-" + scenario + "@example.test", null); var otherProfile = new FarmerProfile(other.Id);
            setup.AddRange(other, otherProfile); await setup.SaveChangesAsync(Token);
            session.CurrentUser.UserId = other.Id;
            await Assert.ThrowsAsync<NotFoundException>(() => Run(s => s.GetAsync(created.Id, true, Token)));
            var otherCustomerList = await Run(s => s.ListAsync(new(), true, Token));
            Assert.Equal(0, otherCustomerList.TotalCount);
            Assert.Equal(new ExchangeSummaryResponse(0m, 0m, 0m), otherCustomerList.Summary);
            session.CurrentUser.UserId = admin.Id; session.CurrentUser.Role = "ADMIN";
            await Run(s => s.ApproveAsync(created.Id, Token)); await Run(s => s.ReceiveAsync(created.Id, Token));
            await Assert.ThrowsAsync<BusinessRuleException>(() => Run(s => s.FinishInspectionAsync(created.Id, Token)));
            foreach (var line in created.Return.Items) await Run(s => s.InspectAsync(created.Id, line.Id, new(damaged ? "DAMAGED" : "RESELLABLE", "Checked"), Token));
            var inspected = await Run(s => s.FinishInspectionAsync(created.Id, Token));
            Assert.Equal(unpaid, inspected.Return.TotalDebtAdjustment); Assert.Equal(200m - unpaid, inspected.Return.TotalRefundAmount);
            Assert.Equal(price - (200m - unpaid), inspected.Difference);
            var unsettledTotals = await Run(s => s.ListAsync(new() { OrderId = order.Id, Status = "READY_TO_COMPLETE" }, false, Token));
            Assert.Equal(1, unsettledTotals.TotalCount);
            Assert.Equal(new ExchangeSummaryResponse(0m, 0m, 0m), unsettledTotals.Summary);
            var replacementLine = Assert.Single(inspected.Replacements);
            var completion = new CompleteExchangeRequest(inspected.Difference!.Value, "RECEIPT-" + scenario, [new(replacementLine.Id, newLot.Id, 1)]);
            await Assert.ThrowsAsync<BusinessRuleException>(() => Run(s => s.CompleteAsync(created.Id, completion with { ConfirmedDifference = 999m }, Token)));
            await Assert.ThrowsAsync<BusinessRuleException>(() => Run(s => s.CompleteAsync(created.Id, completion with { Lots = [new(replacementLine.Id, oldLot.Id, 1)] }, Token)));
            Assert.Equal("READY_TO_COMPLETE", (await Detail(created.Id)).Status);
            await using (var check = session.NewContext()) Assert.Equal(0, await check.Orders.CountAsync(o => o.Id != order.Id && o.Note == "Exchange for " + order.OrderNumber, Token));
            await using (var block = session.NewContext())
            {
                var blocked = await block.InventoryLots.SingleAsync(l => l.Id == newLot.Id, Token);
                blocked.ChangeStatus(InventoryLotStatus.Quarantined); await block.SaveChangesAsync(Token);
            }
            await Assert.ThrowsAsync<BusinessRuleException>(() => Run(s => s.CompleteAsync(created.Id, completion, Token)));
            await using (var unblock = session.NewContext())
            {
                var blocked = await unblock.InventoryLots.SingleAsync(l => l.Id == newLot.Id, Token);
                blocked.ChangeStatus(InventoryLotStatus.Active); await unblock.SaveChangesAsync(Token);
            }
            var finished = await Run(s => s.CompleteAsync(created.Id, completion, Token));
            Assert.Equal("COMPLETED", finished.Status); Assert.NotNull(finished.ReplacementOrderId);
            Assert.Equal(finished.ReplacementOrderId, (await Run(s => s.CompleteAsync(created.Id, completion, Token))).ReplacementOrderId);
            await using var verify = session.NewContext();
            Assert.Equal(98 + (damaged ? 0 : 2), (await verify.InventoryLots.Include(l => l.Balance).SingleAsync(l => l.Id == oldLot.Id, Token)).Balance.QuantityOnHand);
            Assert.Equal(9, (await verify.InventoryLots.Include(l => l.Balance).SingleAsync(l => l.Id == newLot.Id, Token)).Balance.QuantityOnHand);
            Assert.Equal(OrderStatus.Completed, (await verify.Orders.SingleAsync(o => o.Id == order.Id, Token)).Status);
            Assert.Equal(200m, (await verify.Orders.SingleAsync(o => o.Id == order.Id, Token)).TotalAmount);
            Assert.Equal(1, await verify.StockMovements.CountAsync(m => m.OrderId == finished.ReplacementOrderId && m.MovementType == StockMovementType.Sale, Token));
            Assert.Equal(1, await verify.Orders.CountAsync(o => o.Id == finished.ReplacementOrderId, Token));
            Assert.Equal(Math.Max(0, inspected.Difference.Value), await verify.Payments.Where(p => p.OrderId == finished.ReplacementOrderId && p.PaymentMethod == PaymentMethod.Cash).SumAsync(p => p.Amount, Token));
            Assert.Equal(Math.Max(0, -inspected.Difference.Value), await verify.Refunds.Where(f => f.SalesReturnId == created.Return.Id && f.RefundMethod == AgriSage.Domain.Features.Returns.Enums.RefundMethod.Cash).SumAsync(f => f.Amount, Token));
            if (account != null) Assert.Equal(0m, (await verify.DebtAccounts.SingleAsync(a => a.Id == account.Id, Token)).CurrentBalance);
        }
        var allExchanges = await Run(s => s.ListAsync(new() { Page = 2, PageSize = 1 }, false, Token));
        Assert.Single(allExchanges.Items);
        Assert.Equal(7, allExchanges.TotalCount); // Four completed, one rejected and two cancelled.
        Assert.Equal(7, allExchanges.TotalPages);
        Assert.Equal(new ExchangeSummaryResponse(100m, 50m, 50m), allExchanges.Summary);
        var completedExchanges = await Run(s => s.ListAsync(new() { Status = "COMPLETED", PageSize = 1 }, false, Token));
        Assert.Equal(4, completedExchanges.TotalCount);
        Assert.Equal(allExchanges.Summary, completedExchanges.Summary);
        var unpaidExchange = await Run(s => s.ListAsync(new() { Search = "OD-EX-4" }, false, Token));
        Assert.Equal(1, unpaidExchange.TotalCount);
        Assert.Equal(new ExchangeSummaryResponse(50m, 0m, 50m), unpaidExchange.Summary);
        var cancelledExchanges = await Run(s => s.ListAsync(new() { Status = "CANCELLED" }, false, Token));
        Assert.Equal(new ExchangeSummaryResponse(0m, 0m, 0m), cancelledExchanges.Summary);
        var emptyExchanges = await Run(s => s.ListAsync(new() { Status = "REQUESTED" }, false, Token));
        Assert.Empty(emptyExchanges.Items);
        Assert.Equal(0, emptyExchanges.TotalCount);
        Assert.Equal(new ExchangeSummaryResponse(0m, 0m, 0m), emptyExchanges.Summary);
        await using (var reports = session.NewContext())
        {
            var today = BusinessCalendar.Today(DateTimeOffset.UtcNow);
            var clock = new Clock(); var scope = new ReportScope(reports, session.CurrentUser, new PermissionEvaluator(reports, session.CurrentUser));
            var service = new OperationalReportService(reports, scope, clock);
            var totals = (await service.PaymentsAsync(new() { FromDate = today, ToDate = today }, Token)).Totals;
            Assert.Equal(750m, totals.ReceivedAmount); // 650 original cash + 100 additional cash, no exchange offsets.
            Assert.Equal(50m, totals.RefundedAmount);
            Assert.Equal(700m, totals.NetReceivedAmount);
            Assert.Equal(50m, (await service.RefundsAsync(new() { FromDate = today, ToDate = today }, Token)).Totals.RefundedAmount);
            Assert.Equal(750m, (await new CounterSalesTodayService(reports, scope, clock).GetAsync(Token)).ReceivedAmount);
        }
        output.WriteLine("All four exchange settlement cases passed; isolated schema and fixtures roll back on disposal.");
    }
}
