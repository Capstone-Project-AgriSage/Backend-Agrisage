using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;
using AgriSage.Domain.Features.Returns.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// REAL PostgreSQL (opt-in AGRISAGE_DB_TESTS=1, needs migration PaymentOrderLinkAndOrderRefunds applied):
// payments.order_id and cancelled-order refunds (database design §35.18), always rolled back.
public class PaymentRefundDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Unique() => Guid.NewGuid().ToString("N")[..10];

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static async Task<(Order Order, Payment Payment, Guid UserId)> CancelledPaidOrderAsync(
        RealDb.Session session, AgriSageDbContext context)
    {
        var (user, store, _) = await session.SeedAsync(context);
        var order = WalkInOrder(store, user.Id);
        order.Cancel(user.Id, Now, "Out of stock");
        var payment = new Payment(
            store.Id, $"PM-{Unique()}", PaymentContext.OrderPayment, PaymentMethod.Cash, 100_000m, Now,
            createdBy: user.Id, orderId: order.Id);
        context.AddRange(order, payment);
        await context.SaveChangesAsync(Token);

        return (order, payment, user.Id);
    }

    private static Order WalkInOrder(Store store, Guid userId) =>
        new(store.Id, $"OD-{Unique()}", OrderSource.Counter, CustomerType.WalkIn, userId, "Walk-in customer",
            SettlementType.FullPayment, FulfillmentType.Pickup);

    [RealDbFact]
    public async Task An_order_payment_and_a_cancelled_order_refund_are_stored_and_read_back()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        var (order, payment, userId) = await CancelledPaidOrderAsync(session, context);

        var refund = order.RequestCancellationRefund($"RF-{Unique()}", payment.Id, RefundMethod.Cash, 100_000m, userId, Now);
        await context.SaveChangesAsync(Token);
        order.CompleteCancellationRefund(refund.Id, userId, Now, "CASH-DESK");
        await context.SaveChangesAsync(Token);

        await using var reader = session.NewContext();
        var storedPayment = await reader.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.Id, Token);
        var storedOrder = await reader.Orders.AsNoTracking().Include(o => o.CancellationRefunds)
            .SingleAsync(o => o.Id == order.Id, Token);
        var storedRefund = Assert.Single(storedOrder.CancellationRefunds);
        Assert.Equal(order.Id, storedPayment.OrderId);
        Assert.Equal(order.Id, storedRefund.OrderId);
        Assert.Null(storedRefund.SalesReturnId);
        Assert.Equal(payment.Id, storedRefund.OriginalPaymentId);
        Assert.Equal(RefundStatus.Completed, storedRefund.Status);
        Assert.Equal("CASH-DESK", storedRefund.ExternalReference);
    }

    [RealDbFact]
    public async Task A_debt_repayment_with_an_order_is_rejected_by_a_check_constraint()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        var (user, store, _) = await session.SeedAsync(context);
        var order = WalkInOrder(store, user.Id);
        var payment = new Payment(store.Id, $"PM-{Unique()}", PaymentContext.DebtRepayment, PaymentMethod.Cash, 1m, Now);
        context.AddRange(order, payment);
        context.Entry(payment).Property(p => p.OrderId).CurrentValue = order.Id;

        var violation = await RealDb.ExpectViolationAsync(() => context.SaveChangesAsync(Token));

        Assert.Equal(PostgresErrorCodes.CheckViolation, violation.SqlState);
        Assert.Equal("ck_payments_order_context", violation.ConstraintName);
    }

    [RealDbFact]
    public async Task An_order_payment_without_an_order_is_rejected_by_a_check_constraint()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        var (_, payment, _) = await CancelledPaidOrderAsync(session, context);
        context.Entry(payment).Property(p => p.OrderId).CurrentValue = null;

        var violation = await RealDb.ExpectViolationAsync(() => context.SaveChangesAsync(Token));

        Assert.Equal("ck_payments_order_context", violation.ConstraintName);
    }

    [RealDbFact]
    public async Task A_refund_without_any_source_is_rejected_by_a_check_constraint()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        var (order, payment, userId) = await CancelledPaidOrderAsync(session, context);
        var refund = order.RequestCancellationRefund($"RF-{Unique()}", payment.Id, RefundMethod.Cash, 1m, userId, Now);
        await context.SaveChangesAsync(Token);

        // Raw SQL: EF would restore order_id from the Order's CancellationRefunds collection.
        var violation = await RealDb.ExpectViolationAsync(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE refunds SET order_id = NULL WHERE id = {refund.Id}", Token));

        Assert.Equal("ck_refunds_source", violation.ConstraintName);
    }

    [RealDbFact]
    public async Task A_cancelled_order_refund_without_its_payment_is_rejected_by_a_check_constraint()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        var (order, payment, userId) = await CancelledPaidOrderAsync(session, context);
        var refund = order.RequestCancellationRefund($"RF-{Unique()}", payment.Id, RefundMethod.Cash, 1m, userId, Now);
        context.Entry(refund).Property(r => r.OriginalPaymentId).CurrentValue = null;

        var violation = await RealDb.ExpectViolationAsync(() => context.SaveChangesAsync(Token));

        Assert.Equal("ck_refunds_order_payment", violation.ConstraintName);
    }
}
