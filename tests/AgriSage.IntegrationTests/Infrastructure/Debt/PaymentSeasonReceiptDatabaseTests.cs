using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// F3.17: the receipt of a debt repayment shows, per season now collecting the debt, what this payment paid and what the customer
// still owes there. Read from payment_allocations and debt_entries; nothing is written.
public sealed class PaymentSeasonReceiptDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<T> Run<T>(CreditTestDatabase e, Func<IServiceProvider, Task<T>> act)
    {
        using var s = e.Scope();
        return await act(s.ServiceProvider);
    }

    // A debt of the given season (or none), entered as a manual debt for exact amounts and due date.
    private static async Task<Guid> DebtAsync(CreditTestDatabase e, Guid farmer, decimal amount, Guid? season, DateOnly due)
    {
        var entry = await Run(e, sp => sp.GetRequiredService<IDebtService>().ManualAsync(farmer, new(amount, due, "Nợ"), Token));
        using var s = e.Scope();
        await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE debt_entries SET origin_crop_season_id = {season}, collection_crop_season_id = {season}, original_due_date = due_date WHERE id = {entry.Id}", Token);
        return entry.Id;
    }

    private static async Task<Guid> OtherFarmerAsync(CreditTestDatabase e)
    {
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Code == RoleCode.Farmer, Token);
        var user = new User(role.Id, "Farmer 2", "hash", $"{Guid.NewGuid():N}@example.test", null);
        var farmer = new FarmerProfile(user.Id);
        db.AddRange(user, farmer, new FarmerCreditProfile(e.StoreId, farmer.Id, 20_000_000m, e.User.UserId!.Value, e.Clock.UtcNow, e.TierId),
            new DebtAccount(e.StoreId, farmer.Id));
        await db.SaveChangesAsync(Token);
        return farmer.Id;
    }

    [CreditDbFact]
    public async Task A_repayment_receipt_lists_paid_and_outstanding_per_season_and_nothing_of_another_customer()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var seasons = await Run(e, sp => sp.GetRequiredService<ICropSeasonService>().CreateAsync(
            new("DX-2026", "Đông Xuân", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, new DateOnly(2026, 10, 1)), Token));
        var summer = await Run(e, sp => sp.GetRequiredService<ICropSeasonService>().CreateAsync(
            new("HT-2027", "Hè Thu", new DateOnly(2027, 7, 15), new DateOnly(2027, 8, 15), 30), Token));
        var other = await OtherFarmerAsync(e);
        await DebtAsync(e, e.FarmerId, 1_000_000m, seasons.Id, new DateOnly(2027, 4, 14));
        await DebtAsync(e, e.FarmerId, 2_000_000m, summer.Id, new DateOnly(2027, 9, 14));
        await DebtAsync(e, e.FarmerId, 250_000m, null, new DateOnly(2027, 12, 1));
        await DebtAsync(e, other, 9_000_000m, seasons.Id, new DateOnly(2027, 4, 14));

        // 1.5M pays the oldest due first: all of Đông Xuân (1M) and 500k of Hè Thu.
        var receipt = await Run(e, sp => sp.GetRequiredService<IPaymentService>()
            .ReceiveCashAsync(new("DEBT_REPAYMENT", 1_500_000m, FarmerProfileId: e.FarmerId), Token));

        Assert.NotNull(receipt.OutstandingBySeason);
        Assert.Equal(
            [("DX-2026", 1_000_000m, 0m), ("HT-2027", 500_000m, 1_500_000m), ((string?)null, 0m, 250_000m)],
            receipt.OutstandingBySeason!.Select(s => (s.SeasonCode, s.PaidAmount, s.OutstandingAmount)).ToList());
        Assert.Equal(receipt.Amount - receipt.UnallocatedAmount, receipt.OutstandingBySeason!.Sum(s => s.PaidAmount));

        // The same receipt read again, and by the customer (own payments only), has the same figures.
        var again = await Run(e, sp => sp.GetRequiredService<PaymentQueries>().GetAsync(receipt.Id, e.FarmerId, Token));
        Assert.Equal(receipt.OutstandingBySeason, again.OutstandingBySeason);
    }

    [CreditDbFact]
    public async Task An_order_payment_has_no_season_balances()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var order = await e.OrderAsync(1, Token);
        var payment = await Run(e, sp => sp.GetRequiredService<IPaymentService>()
            .ReceiveCashAsync(new("ORDER_PAYMENT", 1_000_000m, OrderId: order.Id), Token));
        Assert.Null(payment.OutstandingBySeason);
    }
}
