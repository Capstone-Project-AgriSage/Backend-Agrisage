using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Credit;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Reports;
using AgriSage.Domain.Features.Credit.Enums;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Returns.Entities;
using AgriSage.Domain.Features.Returns.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// Each test migrates and disposes its own schema on an explicitly configured test database.
// Concurrency cases use separate connections and real commits; no shared production/development data is touched.
public sealed class CreditDebtDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [CreditDbFact]
    public async Task Eligibility_checks_customer_state_limit_reservations_and_exact_boundary()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        using var s = e.Scope(); var services = s.ServiceProvider; var db = services.GetRequiredService<AgriSageDbContext>();
        var check = services.GetRequiredService<ICreditEligibilityService>();
        Assert.True((await check.CheckAsync(e.FarmerId, 20_000_000, Token)).Eligible);
        Assert.Equal("INSUFFICIENT_CREDIT", (await check.CheckAsync(e.FarmerId, 20_000_000.01m, Token)).ReasonCode);
        Assert.Equal("CUSTOMER_NOT_FOUND", (await check.CheckAsync(Guid.NewGuid(), 1, Token)).ReasonCode);
        var user = await db.Users.SingleAsync(u => u.Id == e.FarmerUserId, Token); user.ChangeStatus(UserStatus.Locked);
        await db.SaveChangesAsync(Token);
        Assert.Equal("CUSTOMER_INACTIVE", (await check.CheckAsync(e.FarmerId, 1, Token)).ReasonCode);
        user.ChangeStatus(UserStatus.Active);
        var profile = await db.FarmerCreditProfiles.SingleAsync(p => p.Id == e.ProfileId, Token); profile.ChangeStatus(FarmerCreditProfileStatus.Suspended);
        await db.SaveChangesAsync(Token);
        Assert.Equal("CREDIT_DISABLED", (await check.CheckAsync(e.FarmerId, 1, Token)).ReasonCode);
        profile.ChangeStatus(FarmerCreditProfileStatus.Active);
        await db.SaveChangesAsync(Token);
        await services.GetRequiredService<ICustomerCreditService>().LimitAsync(e.FarmerId, new(0, "zero"), Token);
        Assert.Equal("NO_CREDIT_LIMIT", (await check.CheckAsync(e.FarmerId, 1, Token)).ReasonCode);
    }
    [CreditDbFact]
    public async Task Concurrent_confirmations_reserve_only_one_of_two_orders_above_remaining_limit()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        using (var s = e.Scope()) await s.ServiceProvider.GetRequiredService<IDebtService>().ManualAsync(e.FarmerId,
            new(10_000_000, BusinessCalendar.Today(e.Clock.UtcNow).AddDays(10), "existing AR"), Token);
        var a = await e.OrderAsync(8, Token); var b = await e.OrderAsync(8, Token);
        async Task<bool> Confirm(Guid id)
        {
            using var s = e.Scope();
            try { await s.ServiceProvider.GetRequiredService<IOrderConfirmationService>().ConfirmAsync(id, Token); return true; }
            catch (BusinessRuleException) { return false; }
        }
        var results = await Task.WhenAll(Confirm(a.Id), Confirm(b.Id));
        Assert.Single(results, r => r);
        using var verify = e.Scope(); var db = verify.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        Assert.Equal(8_000_000, await db.CreditReservations.SumAsync(r => r.AmountReserved - r.AmountConsumed - r.AmountReleased, Token));
        Assert.Equal(10_000_000, await db.DebtAccounts.Where(d => d.Id == e.AccountId).Select(d => d.CurrentBalance).SingleAsync(Token));
        Assert.Equal(8, await db.InventoryLotBalances.Where(l => l.InventoryLotId == e.LotId).Select(l => l.QuantityReserved).SingleAsync(Token));
    }
    [CreditDbFact]
    public async Task Pickup_creates_debt_with_vietnam_due_date_and_duplicate_trigger_is_harmless()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var order = await e.FulfillAsync(10, Token);
        using var s = e.Scope(); var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var debt = await db.DebtEntries.SingleAsync(Token);
        Assert.Equal(order.Id, debt.OrderId); Assert.Equal(10_000_000, debt.OriginalAmount);
        Assert.Equal(new DateOnly(2026, 11, 4), debt.DueDate); // UTC Oct 4 17:30 = Vietnam Oct 5, +30 days.
        var tracked = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id, Token);
        await using var tx = await db.BeginTransactionAsync(Token);
        await s.ServiceProvider.GetRequiredService<IFulfillmentFinancialPosting>().PostAsync(new(tracked,
            [new(tracked.Items.Single().Id, 10, 10_000_000)], FulfillmentSource.Pickup, null, null,
            debt.SourceStockMovementId!.Value, e.User.UserId!.Value, e.Clock.UtcNow), Token);
        await db.SaveChangesAsync(Token); await tx.CommitAsync(Token);
        Assert.Equal(1, await db.DebtEntries.CountAsync(Token));
        Assert.Equal(1, await db.DebtTransactions.CountAsync(t => t.TransactionType == DebtTransactionType.CreditSale, Token));
        Assert.Equal(10_000_000, (await s.ServiceProvider.GetRequiredService<IDebtService>().EntryAsync(debt.Id, Token)).Customer.CurrentOutstandingDebt);
    }
    [CreditDbFact]
    public async Task Partial_fulfillments_and_prepayment_only_post_unpaid_value()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var order = await e.OrderAsync(10, Token);
        using (var s = e.Scope()) await s.ServiceProvider.GetRequiredService<IPaymentService>().ReceiveCashAsync(new("ORDER_PAYMENT", 2_000_000, order.Id), Token);
        using (var s = e.Scope()) await s.ServiceProvider.GetRequiredService<IOrderConfirmationService>().ConfirmAsync(order.Id, Token);
        using (var s = e.Scope()) await s.ServiceProvider.GetRequiredService<IPaymentService>().ReceiveCashAsync(new("ORDER_PAYMENT", 1_000_000, order.Id), Token);
        foreach (var amount in new long[] { 4, 6 })
        {
            using var s = e.Scope();
            await s.ServiceProvider.GetRequiredService<IOrderPickupService>().PickupAsync(order.Id, new([new(order.Items.Single().Id, [new(e.LotId, amount)])]), Token);
        }
        using var verify = e.Scope(); var db = verify.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var debts = await db.DebtEntries.OrderBy(d => d.CreatedAt).ThenBy(d => d.Id).ToListAsync(Token);
        Assert.Equal(new[] { 1_000_000m, 6_000_000m }, debts.Select(d => d.OriginalAmount));
        var reservation = await db.CreditReservations.SingleAsync(Token);
        Assert.Equal(7_000_000, reservation.AmountConsumed); Assert.Equal(1_000_000, reservation.AmountReleased); Assert.Equal(0, reservation.RemainingAmount);
        Assert.Equal(7_000_000, (await db.DebtAccounts.SingleAsync(Token)).CurrentBalance);
    }
    [CreditDbFact]
    public async Task Multiple_cash_payments_recalculate_paid_outstanding_status_and_ledger()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token); await e.FulfillAsync(10, Token);
        foreach (var amount in new[] { 3_000_000m, 5_000_000m, 2_000_000m })
        { using var s = e.Scope(); await s.ServiceProvider.GetRequiredService<IPaymentService>().ReceiveCashAsync(new("DEBT_REPAYMENT", amount, FarmerProfileId: e.FarmerId), Token); }
        using var verify = e.Scope(); var db = verify.ServiceProvider.GetRequiredService<AgriSageDbContext>(); var entry = await db.DebtEntries.SingleAsync(Token);
        var detail = await verify.ServiceProvider.GetRequiredService<IDebtService>().EntryAsync(entry.Id, Token);
        Assert.Equal("PAID", detail.Status); Assert.Equal(0, detail.OutstandingAmount); Assert.Equal(10_000_000, detail.TotalPaid);
        Assert.Equal(3, detail.Payments.Count); Assert.Equal(4, detail.Transactions.Count);
        await Assert.ThrowsAsync<BusinessRuleException>(() => verify.ServiceProvider.GetRequiredService<IPaymentService>().ReceiveCashAsync(new("DEBT_REPAYMENT", 1, FarmerProfileId: e.FarmerId), Token));
    }
    [CreditDbFact]
    public async Task Concurrent_cash_payments_cannot_overpay()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token); await e.FulfillAsync(5, Token);
        async Task<bool> Pay()
        {
            using var s = e.Scope();
            try { await s.ServiceProvider.GetRequiredService<IPaymentService>().ReceiveCashAsync(new("DEBT_REPAYMENT", 4_000_000, FarmerProfileId: e.FarmerId), Token); return true; }
            catch (BusinessRuleException) { return false; }
        }
        Assert.Single(await Task.WhenAll(Pay(), Pay()), r => r);
        using var verify = e.Scope(); var db = verify.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        Assert.Equal(1_000_000, (await db.DebtEntries.SingleAsync(Token)).OutstandingAmount);
        Assert.Equal(1, await db.Payments.CountAsync(Token));
    }
    [CreditDbFact]
    public async Task Bank_pending_rejection_and_repeated_concurrent_confirmation_only_post_once()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token); await e.FulfillAsync(10, Token);
        Guid paymentId;
        using (var s = e.Scope())
        {
            var bank = s.ServiceProvider.GetRequiredService<IBankDebtPaymentService>();
            var pending = await bank.RecordAsync(new(e.FarmerId, 3_000_000, Reference: "bank-ref"), Token); paymentId = pending.Id;
            var rejected = await bank.RecordAsync(new(e.FarmerId, 1_000_000), Token);
            await bank.RejectAsync(rejected.Id, new("Wrong reference"), Token);
            Assert.Equal(10_000_000, (await s.ServiceProvider.GetRequiredService<IDebtService>().AccountAsync(e.FarmerId, Token)).CurrentBalance);
        }
        async Task Confirm() { using var s = e.Scope(); await s.ServiceProvider.GetRequiredService<IBankDebtPaymentService>().ConfirmAsync(paymentId, Token); }
        await Task.WhenAll(Confirm(), Confirm());
        using var verify = e.Scope(); var db = verify.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        Assert.Equal(7_000_000, (await db.DebtAccounts.SingleAsync(Token)).CurrentBalance);
        Assert.Equal(1, await db.DebtTransactions.CountAsync(t => t.TransactionType == DebtTransactionType.Payment, Token));
        Assert.Equal(1, await db.PaymentAllocations.CountAsync(Token));
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "DEBT_PAYMENT_REJECTED", Token));
    }
    [CreditDbFact]
    public async Task Failed_bank_confirmation_rolls_back_status_and_does_not_create_a_ledger_row()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token); await e.FulfillAsync(5, Token);
        Guid id;
        using (var s = e.Scope()) id = (await s.ServiceProvider.GetRequiredService<IBankDebtPaymentService>().RecordAsync(new(e.FarmerId, 4_000_000), Token)).Id;
        using (var s = e.Scope()) await s.ServiceProvider.GetRequiredService<IPaymentService>().ReceiveCashAsync(new("DEBT_REPAYMENT", 2_000_000, FarmerProfileId: e.FarmerId), Token);
        using (var s = e.Scope()) await Assert.ThrowsAsync<BusinessRuleException>(() => s.ServiceProvider.GetRequiredService<IBankDebtPaymentService>().ConfirmAsync(id, Token));
        using var verify = e.Scope(); var p = await verify.ServiceProvider.GetRequiredService<IPaymentService>().GetAsync(id, Token);
        Assert.Equal("PENDING", p.Status); Assert.Empty(p.Allocations);
        Assert.Equal(3_000_000, (await verify.ServiceProvider.GetRequiredService<IDebtService>().AccountAsync(e.FarmerId, Token)).CurrentBalance);
    }
    [CreditDbFact]
    public async Task Due_today_is_not_overdue_and_policy_only_blocks_after_due_date()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token); await e.FulfillAsync(5, Token);
        using var s = e.Scope(); var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var entry = await db.DebtEntries.SingleAsync(Token); var service = s.ServiceProvider.GetRequiredService<IDebtService>();
        e.Clock.UtcNow = BusinessCalendar.StartOfDay(entry.DueDate);
        Assert.False((await service.EntryAsync(entry.Id, Token)).IsOverdue);
        e.Clock.UtcNow = e.Clock.UtcNow.AddDays(1);
        Assert.Equal(1, (await service.EntryAsync(entry.Id, Token)).OverdueDays);
        var blocking = new CreditEligibilityService(db, e.Clock, Options.Create(new CreditPolicy { BlockCreditWhenOverdue = true }));
        Assert.Equal("OVERDUE_DEBT", (await blocking.CheckAsync(e.FarmerId, 1, Token)).ReasonCode);
        Assert.True((await s.ServiceProvider.GetRequiredService<ICreditEligibilityService>().CheckAsync(e.FarmerId, 1, Token)).Eligible);
        var dashboard = await service.DashboardAsync(Token);
        Assert.Equal(5_000_000, dashboard.TotalOverdueDebt); Assert.Single(dashboard.OverdueDebts);
    }
    [CreditDbFact]
    public async Task Cancellation_releases_credit_without_creating_debt()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token); var order = await e.OrderAsync(5, Token);
        using (var s = e.Scope()) await s.ServiceProvider.GetRequiredService<IOrderConfirmationService>().ConfirmAsync(order.Id, Token);
        using (var s = e.Scope()) await s.ServiceProvider.GetRequiredService<IOrderCancellationService>().CancelAsync(order.Id, new("cancel"), Token);
        using var verify = e.Scope(); var db = verify.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        Assert.Empty(await db.DebtEntries.ToListAsync(Token)); Assert.Equal(0, (await db.CreditReservations.SingleAsync(Token)).RemainingAmount);
    }
    [CreditDbFact]
    public async Task Full_payment_requires_funding_and_creates_no_debt_at_pickup()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        OrderResponse order;
        using (var s = e.Scope()) order = await s.ServiceProvider.GetRequiredService<IOrderService>().CreateAsync(
            new("REGISTERED", "FULL_PAYMENT", "PICKUP", [new(e.StoreProductId, e.PackagingId, 2)], e.FarmerId), Token);
        using (var s = e.Scope()) await Assert.ThrowsAsync<BusinessRuleException>(() => s.ServiceProvider.GetRequiredService<IOrderConfirmationService>().ConfirmAsync(order.Id, Token));
        using (var s = e.Scope()) await s.ServiceProvider.GetRequiredService<IPaymentService>().ReceiveCashAsync(new("ORDER_PAYMENT", 2_000_000, order.Id), Token);
        using (var s = e.Scope()) await s.ServiceProvider.GetRequiredService<IOrderConfirmationService>().ConfirmAsync(order.Id, Token);
        using (var s = e.Scope()) await s.ServiceProvider.GetRequiredService<IOrderPickupService>().PickupAsync(order.Id, new([new(order.Items.Single().Id, [new(e.LotId, 2)])]), Token);
        using var verify = e.Scope(); var db = verify.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        Assert.Equal(0, await db.DebtEntries.CountAsync(Token)); Assert.Equal(0, await db.CreditReservations.CountAsync(Token));
    }
    [CreditDbFact]
    public async Task Other_store_debt_and_other_customer_entry_are_hidden()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        using var s = e.Scope(); var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var otherStore = new Store("OTHER", "Other", "Test", "Test", status: StoreStatus.Inactive);
        var account = new Domain.Features.Debt.Entities.DebtAccount(otherStore.Id, e.FarmerId);
        var posting = account.CreateManualAdjustmentEntry("FOREIGN", 100, new(2026, 11, 1), e.User.UserId!.Value, e.Clock.UtcNow);
        db.AddRange(otherStore, account, posting.Entry, posting.Transaction); await db.SaveChangesAsync(Token);
        var debt = s.ServiceProvider.GetRequiredService<IDebtService>();
        await Assert.ThrowsAsync<NotFoundException>(() => debt.EntryAsync(posting.Entry.Id, Token));
        Assert.Empty((await debt.EntriesAsync(new(), Token)).Items);
        var order = await e.FulfillAsync(1, Token); var entry = await db.DebtEntries.SingleAsync(x => x.OrderId == order.Id, Token);
        await Assert.ThrowsAsync<NotFoundException>(() => debt.RequireOwnedEntryAsync(entry.Id, Guid.NewGuid(), Token));
    }
    [CreditDbFact]
    public async Task Reports_use_historical_ledger_and_confirmed_collection_totals()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token); await e.FulfillAsync(5, Token);
        var firstDay = BusinessCalendar.Today(e.Clock.UtcNow); e.Clock.UtcNow = e.Clock.UtcNow.AddDays(40);
        using (var s = e.Scope()) await s.ServiceProvider.GetRequiredService<IPaymentService>().ReceiveCashAsync(new("DEBT_REPAYMENT", 2_000_000, FarmerProfileId: e.FarmerId), Token);
        using var verify = e.Scope(); var reports = verify.ServiceProvider.GetRequiredService<IDebtReportService>();
        Assert.Equal(5_000_000, (await reports.AgingAsync(new(firstDay), Token)).Totals.NotDue);
        Assert.Equal(3_000_000, (await reports.AgingAsync(new(), Token)).Totals.Days1To30);
        var collection = await reports.CollectionsAsync(new() { FromDate = firstDay, ToDate = BusinessCalendar.Today(e.Clock.UtcNow), GroupBy = "METHOD" }, Token);
        Assert.Equal(2_000_000, collection.Totals.CollectedAmount); Assert.Equal("CASH", Assert.Single(collection.Rows).Key);
        Assert.Equal(3_000_000, Assert.Single((await reports.GroupsAsync(Token)).Rows).Outstanding);
    }
    [CreditDbFact]
    public async Task Return_adjustment_reduces_debt_and_retries_preserve_original()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token); var response = await e.FulfillAsync(10, Token);
        using var s = e.Scope(); var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var order = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == response.Id, Token);
        var movement = await db.StockMovements.Include(m => m.Items).SingleAsync(m => m.OrderId == order.Id, Token);
        var r = new SalesReturn(order, "RT-TEST", e.User.UserId!.Value, e.Clock.UtcNow);
        r.AddItem(order, order.Items.Single(), 3, 0, e.LotId, "RETURN", originalStockMovementItemId: movement.Items.Single().Id);
        r.Approve(e.User.UserId.Value, e.Clock.UtcNow); r.MarkReceived(e.User.UserId.Value, e.Clock.UtcNow);
        db.SalesReturns.Add(r); await db.SaveChangesAsync(Token);
        await using var tx = await db.BeginTransactionAsync(Token);
        var posting = s.ServiceProvider.GetRequiredService<IDebtReturnPosting>();
        Assert.Equal(3_000_000, await posting.ApplyReturnAsync(order.Id, r.Id, r.TotalReturnAmount, e.User.UserId.Value, movement.Id, Token));
        Assert.Equal(3_000_000, await posting.ApplyReturnAsync(order.Id, r.Id, r.TotalReturnAmount, e.User.UserId.Value, movement.Id, Token));
        await db.SaveChangesAsync(Token); await tx.CommitAsync(Token);
        var entry = await db.DebtEntries.SingleAsync(Token);
        Assert.Equal(10_000_000, entry.OriginalAmount); Assert.Equal(7_000_000, entry.OutstandingAmount);
        Assert.Equal(1, await db.DebtTransactions.CountAsync(t => t.TransactionType == DebtTransactionType.Return, Token));
    }
}
