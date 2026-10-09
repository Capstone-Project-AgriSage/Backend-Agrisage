using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Tests.TestDoubles;
using AgriSage.Application.Features.Carts;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Pricing;
using AgriSage.Domain.Features.Payments.Enums;
using AgriSage.Infrastructure.Payments;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using AgriSage.IntegrationTests.Infrastructure.Payments;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgriSage.IntegrationTests.Infrastructure.OnlineOrders;

// FLOW_2 §6 (task F2.4) against PostgreSQL with a fake payOS (§10: real payOS only opt-in); every session rolls back.
// Two webhooks of the same payment at the same moment are serialized by the payment row lock (LockPaymentAsync); that
// race cannot be reproduced inside one rolled-back session, so the duplicate is proven sequentially here.
[Collection(RealDb.WalkInPriceListCollection)]
public class PayOsPaymentDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Env(OnlineOrderTestData data, FakePaymentGateway gateway)
    {
        private readonly NpgsqlErrorClassifier _errors = new();
        private readonly DateTimeProvider _clock = new();

        public OnlineOrderTestData Data { get; } = data;

        public FakePaymentGateway Gateway { get; } = gateway;

        private AuditTrail Audit => new(Data.Context, Data.User, _clock);

        private RowLockService Locks => new(Data.Context);

        public PayOsPaymentService PayOs => new(Data.Context, Gateway, Locks, new OrderPrepaymentLedger(Data.Context),
            new PaymentAllocator(_clock, new StubCreditReservationAdjuster(), new StubDebtRepaymentPosting()),
            new PaymentQueries(Data.Context), new CurrentFarmer(Data.Context, Data.User), Data.User, _clock, _errors, Audit);

        // The same service wired the way PayOS:Mode=Simulated wires it: the simulated gateway is both the gateway and the
        // simulator behind the test button. Built per call, like PayOs, so it sees the current acting user.
        public PayOsPaymentService PayOsWith(SimulatedPaymentGateway simulated) => new(Data.Context, simulated, Locks,
            new OrderPrepaymentLedger(Data.Context),
            new PaymentAllocator(_clock, new StubCreditReservationAdjuster(), new StubDebtRepaymentPosting()),
            new PaymentQueries(Data.Context), new CurrentFarmer(Data.Context, Data.User), Data.User, _clock, _errors, Audit, simulated);

        public CartService Carts => new(Data.Context, new CurrentFarmer(Data.Context, Data.User), new PriceResolver(Data.Context),
            Locks, _errors, Data.User, _clock);

        public MeOrderService MyOrders
        {
            get
            {
                var builder = new OrderBuilder(Data.Context, new PriceResolver(Data.Context), Data.User, _clock, Audit, new AgriSage.Application.Features.Credit.CreditEligibilityService(Data.Context, _clock, Microsoft.Extensions.Options.Options.Create(new AgriSage.Application.Features.Credit.CreditPolicy())));
                var queries = new OrderQueries(Data.Context);
                return new MeOrderService(Data.Context, new CurrentFarmer(Data.Context, Data.User), builder, queries,
                    new OrderService(Data.Context, builder, queries, Data.User, _clock, _errors, Audit),
                    new OrderCanceller(Data.Context, Locks, new StubOrderSettlementGuard(),
                        new OrderPaymentCancellation(Data.Context, Locks, Gateway, _clock, Audit)),
                    Locks, _clock, _errors, Audit);
            }
        }

        public async Task<PaymentResponseSnapshot> PaymentAsync(Guid id)
        {
            var p = await Data.Context.Payments.AsNoTracking().Include(x => x.Allocations).SingleAsync(x => x.Id == id, Token);
            return new PaymentResponseSnapshot(p.Status, p.ConfirmationSource, p.ProviderTransactionId, p.CheckoutUrl, p.ProviderOrderCode!.Value,
                p.Allocations.Count(a => !a.IsDeleted && a.AllocationType == PaymentAllocationType.Order), p.ProviderMetadata);
        }
    }

    private sealed record PaymentResponseSnapshot(PaymentStatus Status, PaymentConfirmationSource? Source, string? TransactionId,
        string? CheckoutUrl, long OrderCode, int OrderAllocations, string? Metadata);

    // A Farmer with an online PICKUP order of 2 boxes (110 000 VND), acting as that Farmer.
    private static async Task<(Env Env, OrderResponse Order, OnlineOrderTestData.Farmer Farmer)> PrepareAsync(RealDb.Session session)
    {
        var data = await OnlineOrderTestData.PrepareAsync(session);
        var product = await data.NewProductAsync();
        var farmer = await data.ActAsNewFarmerAsync();
        var env = new Env(data, new FakePaymentGateway());
        await env.Carts.AddItemAsync(new(product.StoreProductId, product.BoxId, 2), Token);
        var order = await env.MyOrders.CheckoutAsync(new("FARMER_WEB", "FULL_PAYMENT", "PICKUP"), Token);

        return (env, order, farmer);
    }

    private static readonly PayOsPaymentRequest PayOrder = new("ORDER_PAYMENT");

    [RealDbFact]
    public async Task Scheduled_reconciliation_records_the_attempt_and_settles_a_missed_webhook_once_as_system()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, order, _) = await PrepareAsync(session);
        var link = await env.PayOs.CreateAsync(PayOrder with { OrderId = order.Id }, Token);
        var service = new PaymentReconciliationService(env.Data.Context, env.PayOs,
            new RowLockService(env.Data.Context), new DateTimeProvider());
        Assert.Contains(link.PaymentId, await service.PendingAsync(100, Token));
        // The worker entry point cannot be invoked as an authenticated HTTP actor.
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ReconcileAsync(link.PaymentId, Token));
        env.Data.User.UserId = null; env.Data.User.Role = null;
        Assert.True(await service.RecordAttemptAsync(link.PaymentId, Token));
        var recorded = await env.Data.Context.Payments.AsNoTracking().SingleAsync(p => p.Id == link.PaymentId, Token);
        Assert.NotNull(recorded.LastReconciliationAttemptAt);
        Assert.Equal(PaymentStatus.Pending, recorded.Status);
        Assert.Equal(0, await env.Data.Context.PaymentAllocations.CountAsync(a => a.PaymentId == link.PaymentId, Token));
        env.Gateway.SetStatus(link.ProviderOrderCode, PaymentLinkStatus.Paid, (long)link.Amount);
        await service.ReconcileAsync(link.PaymentId, Token);
        await service.ReconcileAsync(link.PaymentId, Token);
        Assert.Equal(PaymentStatus.Paid, (await env.PaymentAsync(link.PaymentId)).Status);
        Assert.Equal(1, (await env.PaymentAsync(link.PaymentId)).OrderAllocations);
        Assert.False(await service.RecordAttemptAsync(link.PaymentId, Token));
        Assert.DoesNotContain(link.PaymentId, await service.PendingAsync(100, Token));
        var log = await env.Data.Context.AuditLogs.SingleAsync(a => a.Action == "PAYMENT_RECEIVED" && a.EntityId == link.PaymentId, Token);
        Assert.Null(log.ActorUserId);
        Assert.Equal(1, await env.Data.Context.NotificationOutbox.CountAsync(o => o.AuditLogId == log.Id, Token));
    }

    [RealDbFact]
    public async Task Provider_failure_keeps_pending_payment_and_allocations_unchanged_for_a_later_retry()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, order, _) = await PrepareAsync(session);
        var link = await env.PayOs.CreateAsync(PayOrder with { OrderId = order.Id }, Token);
        var service = new PaymentReconciliationService(env.Data.Context, env.PayOs,
            new RowLockService(env.Data.Context), new DateTimeProvider());
        env.Data.User.UserId = null; env.Data.User.Role = null;
        await service.RecordAttemptAsync(link.PaymentId, Token);
        env.Gateway.Unavailable = true;
        await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(() => service.ReconcileAsync(link.PaymentId, Token));
        Assert.Equal(PaymentStatus.Pending, (await env.PaymentAsync(link.PaymentId)).Status);
        Assert.Equal(0, (await env.PaymentAsync(link.PaymentId)).OrderAllocations);
        Assert.Contains(link.PaymentId, await service.PendingAsync(100, Token));
        env.Gateway.Unavailable = false;
        env.Gateway.SetStatus(link.ProviderOrderCode, PaymentLinkStatus.Expired);
        await service.ReconcileAsync(link.PaymentId, Token);
        Assert.Equal(PaymentStatus.Failed, (await env.PaymentAsync(link.PaymentId)).Status);
    }

    [RealDbFact]
    public Task Reconciliation_rejects_a_provider_response_with_the_wrong_order_code() => RejectMismatchAsync(true);

    [RealDbFact]
    public Task Reconciliation_rejects_a_provider_response_with_the_wrong_amount() => RejectMismatchAsync(false);

    private async Task RejectMismatchAsync(bool wrongCode)
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, order, _) = await PrepareAsync(session);
        var link = await env.PayOs.CreateAsync(PayOrder with { OrderId = order.Id }, Token);
        var service = new PaymentReconciliationService(env.Data.Context, env.PayOs,
            new RowLockService(env.Data.Context), new DateTimeProvider());
        env.Data.User.UserId = null; env.Data.User.Role = null;
        env.Gateway.QueryOverride = new PaymentLinkState(link.ProviderOrderCode + (wrongCode ? 1 : 0),
            PaymentLinkStatus.Paid, (long)link.Amount + (wrongCode ? 0 : 1), (long)link.Amount);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.ReconcileAsync(link.PaymentId, Token));
        Assert.Equal(PaymentStatus.Pending, (await env.PaymentAsync(link.PaymentId)).Status);
        Assert.Equal(0, (await env.PaymentAsync(link.PaymentId)).OrderAllocations);
    }

    [RealDbFact]
    public async Task A_paid_webhook_confirms_and_allocates_once_even_when_delivered_twice()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, order, _) = await PrepareAsync(session);

        var link = await env.PayOs.CreateAsync(PayOrder with { OrderId = order.Id }, Token);
        Assert.Equal(110_000m, link.Amount); // defaults to what is left to pay
        Assert.Equal("PENDING", link.Status);
        Assert.Equal($"https://pay.example/{link.ProviderOrderCode}", link.CheckoutUrl);
        Assert.Equal($"qr-{link.ProviderOrderCode}", link.QrCode);
        var created = Assert.Single(env.Gateway.Created);
        Assert.Equal(110_000L, created.Amount);
        Assert.Equal(link.PaymentNumber.Replace("-", ""), created.Description);
        Assert.True(created.Description.Length <= 25);
        Assert.InRange(link.ProviderOrderCode, 1, 9_007_199_254_740_991);

        var payload = FakePaymentGateway.Webhook(link.ProviderOrderCode, 110_000m);
        Assert.Equal(PayOsWebhookOutcome.Paid, (await env.PayOs.HandleWebhookAsync(payload, Token)).Outcome);
        Assert.Equal(PayOsWebhookOutcome.AlreadyPaid, (await env.PayOs.HandleWebhookAsync(payload, Token)).Outcome);

        var paid = await env.PaymentAsync(link.PaymentId);
        Assert.Equal(PaymentStatus.Paid, paid.Status);
        Assert.Equal(PaymentConfirmationSource.PayOsWebhook, paid.Source);
        Assert.Equal($"FT{link.ProviderOrderCode}", paid.TransactionId);
        Assert.Equal(1, paid.OrderAllocations);
        Assert.Contains("\"orderCode\"", paid.Metadata);

        // Nothing is left to pay now.
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.PayOs.CreateAsync(PayOrder with { OrderId = order.Id }, Token));
    }

    [RealDbFact]
    public async Task Registration_bad_signature_and_unknown_codes_change_nothing()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, order, _) = await PrepareAsync(session);
        var link = await env.PayOs.CreateAsync(PayOrder with { OrderId = order.Id }, Token);

        Assert.Equal(PayOsWebhookOutcome.Registration, (await env.PayOs.HandleWebhookAsync("{}", Token)).Outcome);
        Assert.Equal(PayOsWebhookOutcome.BadSignature, (await env.PayOs.HandleWebhookAsync("{\"data\":{},\"signature\":\"x\"}", Token)).Outcome);
        Assert.Equal(PayOsWebhookOutcome.UnknownOrderCode,
            (await env.PayOs.HandleWebhookAsync(FakePaymentGateway.Webhook(123, 110_000m), Token)).Outcome);
        Assert.Equal(PaymentStatus.Pending, (await env.PaymentAsync(link.PaymentId)).Status);
    }

    [RealDbFact]
    public async Task An_amount_mismatch_or_overpayment_stays_pending_and_a_failed_transfer_fails_the_payment()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, order, _) = await PrepareAsync(session);
        var link = await env.PayOs.CreateAsync(PayOrder with { OrderId = order.Id, Amount = 50_000m }, Token);

        Assert.Equal(PayOsWebhookOutcome.AmountMismatch,
            (await env.PayOs.HandleWebhookAsync(FakePaymentGateway.Webhook(link.ProviderOrderCode, 40_000m), Token)).Outcome);
        Assert.Equal(PayOsWebhookOutcome.AmountMismatch,
            (await env.PayOs.HandleWebhookAsync(FakePaymentGateway.Webhook(link.ProviderOrderCode, 60_000m), Token)).Outcome);
        Assert.Equal(PaymentStatus.Pending, (await env.PaymentAsync(link.PaymentId)).Status);

        Assert.Equal(PayOsWebhookOutcome.NotPaid,
            (await env.PayOs.HandleWebhookAsync(FakePaymentGateway.Webhook(link.ProviderOrderCode, 50_000m, paid: false), Token)).Outcome);
        Assert.Equal(PaymentStatus.Failed, (await env.PaymentAsync(link.PaymentId)).Status);
    }

    [RealDbFact]
    public async Task Fractional_amounts_and_amounts_above_what_is_left_are_refused()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, order, _) = await PrepareAsync(session);

        await Assert.ThrowsAsync<BusinessRuleException>(() => env.PayOs.CreateAsync(PayOrder with { OrderId = order.Id, Amount = 10_000.5m }, Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.PayOs.CreateAsync(PayOrder with { OrderId = order.Id, Amount = 110_001m }, Token));
        Assert.Empty(env.Gateway.Created);
        Assert.False(await env.Data.Context.Payments.AsNoTracking().AnyAsync(p => p.OrderId == order.Id, Token));
    }

    [RealDbFact]
    public async Task Sync_applies_the_payos_status()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, order, _) = await PrepareAsync(session);

        var expired = await env.PayOs.CreateAsync(PayOrder with { OrderId = order.Id, Amount = 10_000m }, Token);
        Assert.Equal("PENDING", (await env.PayOs.SyncAsync(expired.PaymentId, Token)).Status); // still pending at payOS
        env.Gateway.SetStatus(expired.ProviderOrderCode, PaymentLinkStatus.Expired);
        Assert.Equal("FAILED", (await env.PayOs.SyncAsync(expired.PaymentId, Token)).Status);

        // A missed webhook: the status query confirms it (decision C-D8).
        var missed = await env.PayOs.CreateAsync(PayOrder with { OrderId = order.Id, Amount = 20_000m }, Token);
        env.Gateway.SetStatus(missed.ProviderOrderCode, PaymentLinkStatus.Paid, 20_000);
        var synced = await env.PayOs.SyncAsync(missed.PaymentId, Token);
        Assert.Equal("PAID", synced.Status);
        Assert.Equal("PAYOS_WEBHOOK", synced.ConfirmationSource);
        Assert.Contains("STATUS_QUERY", (await env.PaymentAsync(missed.PaymentId)).Metadata);
    }

    private static SimulatedPaymentGateway NewSimulator() => new(
        Options.Create(new PayOsOptions { ReturnUrl = "http://localhost:5174/payments/payos/return" }),
        TimeProvider.System, NullLogger<SimulatedPaymentGateway>.Instance);

    // The quick "pay" button of the test environment: the simulated link is paid and the payment is settled through the real
    // status-query path, so the allocation, the metadata and the audit are the normal ones. The link id marks it as simulated.
    [RealDbFact]
    public async Task The_test_button_pays_a_simulated_link_through_the_real_status_path_once()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, order, _) = await PrepareAsync(session);
        var simulator = NewSimulator();

        var link = await env.PayOsWith(simulator).CreateAsync(PayOrder with { OrderId = order.Id }, Token);
        Assert.Equal("PENDING", link.Status);
        Assert.Equal($"http://localhost:5174/payments/payos/return?simulated=1&orderCode={link.ProviderOrderCode}", link.CheckoutUrl);
        var pending = await env.PaymentAsync(link.PaymentId);
        Assert.Equal((PaymentStatus.Pending, 0), (pending.Status, pending.OrderAllocations));

        var paid = await env.PayOsWith(simulator).SimulatePaidAsync(link.PaymentId, Token);

        Assert.Equal("PAID", paid.Status);
        var after = await env.PaymentAsync(link.PaymentId);
        Assert.Equal((PaymentStatus.Paid, PaymentConfirmationSource.PayOsWebhook, 1), (after.Status, after.Source, after.OrderAllocations));
        Assert.Contains("STATUS_QUERY", after.Metadata);
        var stored = await env.Data.Context.Payments.AsNoTracking().SingleAsync(p => p.Id == link.PaymentId, Token);
        Assert.StartsWith(SimulatedPaymentGateway.LinkIdPrefix, stored.ProviderPaymentLinkId);
        Assert.Equal(1, await env.Data.Context.AuditLogs.AsNoTracking().CountAsync(a => a.EntityId == link.PaymentId && a.Action == "PAYMENT_RECEIVED", Token));

        // Pressing it again changes nothing.
        var again = await env.PayOsWith(simulator).SimulatePaidAsync(link.PaymentId, Token);
        Assert.Equal("PAID", again.Status);
        Assert.Equal(1, (await env.PaymentAsync(link.PaymentId)).OrderAllocations);
        Assert.Equal(1, await env.Data.Context.AuditLogs.AsNoTracking().CountAsync(a => a.EntityId == link.PaymentId && a.Action == "PAYMENT_RECEIVED", Token));
    }

    [RealDbFact]
    public async Task The_test_button_is_refused_for_other_farmers_closed_payments_a_restart_and_a_real_gateway()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, order, farmer) = await PrepareAsync(session);
        var simulator = NewSimulator();
        var link = await env.PayOsWith(simulator).CreateAsync(PayOrder with { OrderId = order.Id }, Token);

        // Not running with the simulated gateway: the feature does not exist.
        await Assert.ThrowsAsync<NotFoundException>(() => env.PayOs.SimulatePaidAsync(link.PaymentId, Token));

        // Someone else's payment is a 404, like sync and cancel.
        await env.Data.ActAsNewFarmerAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => env.PayOsWith(simulator).SimulatePaidAsync(link.PaymentId, Token));
        env.Data.ActAs(farmer.UserId, "FARMER");

        // A restart forgets the simulated links: the payment cannot be paid and has to be started again.
        var restarted = await Assert.ThrowsAsync<BusinessRuleException>(() => env.PayOsWith(NewSimulator()).SimulatePaidAsync(link.PaymentId, Token));
        Assert.Contains("restarted", restarted.Message);
        Assert.Equal(PaymentStatus.Pending, (await env.PaymentAsync(link.PaymentId)).Status);

        // A cancelled payment is closed for good.
        Assert.Equal("CANCELLED", (await env.PayOsWith(simulator).CancelMineAsync(link.PaymentId, Token)).Status);
        var closed = await Assert.ThrowsAsync<BusinessRuleException>(() => env.PayOsWith(simulator).SimulatePaidAsync(link.PaymentId, Token));
        Assert.Contains("CANCELLED", closed.Message);
        Assert.Equal(0, (await env.PaymentAsync(link.PaymentId)).OrderAllocations);
    }

    [RealDbFact]
    public async Task A_new_link_replaces_the_pending_one_and_a_farmer_cancels_only_their_own()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, order, farmer) = await PrepareAsync(session);
        var first = await env.PayOs.CreateAsync(PayOrder with { OrderId = order.Id }, Token);
        var second = await env.PayOs.CreateAsync(PayOrder with { OrderId = order.Id }, Token);

        Assert.Contains(first.ProviderOrderCode, env.Gateway.Cancelled);
        Assert.Equal(PaymentStatus.Cancelled, (await env.PaymentAsync(first.PaymentId)).Status);

        await env.Data.ActAsNewFarmerAsync(); // someone else
        await Assert.ThrowsAsync<NotFoundException>(() => env.PayOs.CancelMineAsync(second.PaymentId, Token));
        await Assert.ThrowsAsync<NotFoundException>(() => env.PayOs.CreateAsync(PayOrder with { OrderId = order.Id }, Token));

        env.Data.ActAs(farmer.UserId, "FARMER");
        Assert.Equal("CANCELLED", (await env.PayOs.CancelMineAsync(second.PaymentId, Token)).Status);
        Assert.Contains(second.ProviderOrderCode, env.Gateway.Cancelled);

        // Money arriving on a cancelled payment is never applied automatically.
        Assert.Equal(PayOsWebhookOutcome.PaidAfterClose,
            (await env.PayOs.HandleWebhookAsync(FakePaymentGateway.Webhook(second.ProviderOrderCode, second.Amount), Token)).Outcome);
        Assert.Equal(PaymentStatus.Cancelled, (await env.PaymentAsync(second.PaymentId)).Status);
    }

    [RealDbFact]
    public async Task When_payos_refuses_the_payment_is_failed_and_the_call_is_unavailable()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, order, _) = await PrepareAsync(session);
        env.Gateway.Unavailable = true;

        await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(() => env.PayOs.CreateAsync(PayOrder with { OrderId = order.Id }, Token));
        var payment = await env.Data.Context.Payments.AsNoTracking().SingleAsync(p => p.OrderId == order.Id, Token);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Null(payment.CheckoutUrl);
    }

    [RealDbFact]
    public async Task Cancelling_the_order_closes_its_pending_link_at_payos()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, order, _) = await PrepareAsync(session);
        var link = await env.PayOs.CreateAsync(PayOrder with { OrderId = order.Id }, Token);

        Assert.Equal("CANCELLED", (await env.MyOrders.CancelAsync(order.Id, new(), Token)).Status);
        Assert.Contains(link.ProviderOrderCode, env.Gateway.Cancelled);
        Assert.Equal(PaymentStatus.Cancelled, (await env.PaymentAsync(link.PaymentId)).Status);
    }

    [RealDbFact]
    public async Task A_debt_repayment_needs_debt()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, _, _) = await PrepareAsync(session);

        // No debt exists before task F3.4: the balance is 0, so any amount is refused.
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.PayOs.CreateAsync(new PayOsPaymentRequest("DEBT_REPAYMENT", Amount: 10_000m), Token));
    }
}
