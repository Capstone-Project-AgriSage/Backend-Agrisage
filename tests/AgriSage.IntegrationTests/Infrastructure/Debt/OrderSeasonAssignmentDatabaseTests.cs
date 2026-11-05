using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// Lead review of the order change (a9b2db5): confirming an order must keep working when the store has no season, a credit order joins
// the season of the day it is confirmed, and an order paid in full is never given a season.
public sealed class OrderSeasonAssignmentDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<T> Run<T>(CreditTestDatabase e, Func<IServiceProvider, Task<T>> act)
    {
        using var s = e.Scope();
        return await act(s.ServiceProvider);
    }

    private static void At(CreditTestDatabase e, int year, int month, int day) =>
        e.Clock.UtcNow = new DateTimeOffset(year, month, day, 5, 0, 0, TimeSpan.Zero);

    private static async Task<CropSeasonResponse[]> SeasonsAsync(CreditTestDatabase e)
    {
        var dx = await Run(e, sp => sp.GetRequiredService<ICropSeasonService>().CreateAsync(
            new("DX-2026", "Đông Xuân", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, new DateOnly(2026, 10, 1)), Token));
        var ht = await Run(e, sp => sp.GetRequiredService<ICropSeasonService>().CreateAsync(
            new("HT-2027", "Hè Thu", new DateOnly(2027, 7, 15), new DateOnly(2027, 8, 15), 30), Token));
        return [dx, ht];
    }

    private static async Task<Guid?> SeasonOfAsync(CreditTestDatabase e, Guid order) =>
        (await Run(e, sp => sp.GetRequiredService<AgriSageDbContext>().Orders.AsNoTracking().SingleAsync(o => o.Id == order, Token))).CropSeasonId;

    private static async Task<Guid> ConfirmedCreditOrderAsync(CreditTestDatabase e)
    {
        var order = await e.OrderAsync(1, Token);
        await Run(e, sp => sp.GetRequiredService<IOrderConfirmationService>().ConfirmAsync(order.Id, Token));
        return order.Id;
    }

    // No season entered at all: the confirmation works as before and the order has no season.
    [CreditDbFact]
    public async Task A_credit_order_is_confirmed_without_a_season_when_the_store_has_none()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var order = await ConfirmedCreditOrderAsync(e);
        Assert.Null(await SeasonOfAsync(e, order));
    }

    // Inside a season's order window the order joins it; confirmed while it is being harvested it joins the next one.
    [CreditDbFact]
    public async Task A_credit_order_joins_the_season_of_the_day_it_is_confirmed()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var seasons = await SeasonsAsync(e);
        At(e, 2026, 12, 1);                                       // Đông Xuân order window (1/10/2026 to 15/2/2027)
        Assert.Equal(seasons[0].Id, await SeasonOfAsync(e, await ConfirmedCreditOrderAsync(e)));
        At(e, 2027, 3, 1);                                        // Đông Xuân is being harvested: the next season takes the order
        Assert.Equal(seasons[1].Id, await SeasonOfAsync(e, await ConfirmedCreditOrderAsync(e)));
    }

    // An order paid in full is not a debt: it is never given a season.
    [CreditDbFact]
    public async Task An_order_paid_in_full_gets_no_season_even_when_one_is_open()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await SeasonsAsync(e);
        At(e, 2026, 12, 1);
        var order = await Run(e, sp => sp.GetRequiredService<IOrderService>().CreateAsync(
            new("REGISTERED", "FULL_PAYMENT", "PICKUP", [new(e.StoreProductId, e.PackagingId, 1)], e.FarmerId), Token));
        await Run(e, sp => sp.GetRequiredService<IPaymentService>().ReceiveCashAsync(new("ORDER_PAYMENT", 1_000_000, order.Id), Token));
        await Run(e, sp => sp.GetRequiredService<IOrderConfirmationService>().ConfirmAsync(order.Id, Token));
        Assert.Null(await SeasonOfAsync(e, order.Id));
    }
}
