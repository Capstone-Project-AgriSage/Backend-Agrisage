using System.Text.Json;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth;
using AgriSage.Application.Features.Customers;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Payments;

// FLOW_2 §6 (task F2.4). payOS is never called inside an open database transaction:
//   create = [tx: check, PENDING payment with its order code] → payOS link → [tx: link saved] (payOS refusal → FAILED, 503);
//   cancel / sync = payOS query (and cancel) → [tx: the link state applied].
// The webhook and the status query apply a payOS state the same way (SettleAsync); a payment confirmed by the status query
// keeps confirmation_source PAYOS_WEBHOOK with provider_metadata.confirmedVia = STATUS_QUERY (decision C-D8). Locks: the
// order first, then the payment (the order cancellation takes them in the same order).
public sealed class PayOsPaymentService(
    IAgriSageDbContext context,
    IPaymentGateway gateway,
    IRowLockService locks,
    IOrderPrepaymentLedger ledger,
    PaymentAllocator allocator,
    PaymentQueries queries,
    CurrentFarmer currentFarmer,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    IDatabaseErrorClassifier databaseErrors,
    AuditTrail audit,
    ISimulatedPaymentGateway? simulator = null) : IPayOsPaymentService
{
    public const string Provider = "PAYOS";

    public async Task<PayOsPaymentResponse> CreateAsync(PayOsPaymentRequest request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        var me = await FarmerOrNullAsync(cancellationToken);
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        EnumText.TryParse<PaymentContext>(request.PaymentContext, out var paymentContext);

        // At most one PENDING payOS payment per order: the previous link is closed first (outside any transaction).
        if (paymentContext == PaymentContext.OrderPayment)
        {
            await CloseOpenLinksAsync(request.OrderId!.Value, me, storeId, cancellationToken);
        }

        Payment payment;
        await using (var transaction = await context.BeginTransactionAsync(cancellationToken))
        {
            payment = paymentContext == PaymentContext.OrderPayment
                ? await NewOrderPaymentAsync(request, me, storeId, actorId, cancellationToken)
                : await NewDebtPaymentAsync(request, me, storeId, actorId, cancellationToken);

            payment.AssignProviderOrderCode(Provider, await NewOrderCodeAsync(cancellationToken));
            context.Payments.Add(payment);
            audit.Record("PAYMENT_LINK_REQUESTED", "PAYMENT", payment.Id, storeId, null,
                new { payment.PaymentNumber, paymentContext = EnumText.Format(paymentContext), payment.Amount, payment.OrderId, payment.ProviderOrderCode });
            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var orderCode = payment.ProviderOrderCode!.Value;
        PaymentLinkResult link;
        try
        {
            // payOS descriptions are at most 25 characters: the payment number without dashes (14).
            link = await gateway.CreatePaymentLinkAsync(
                new CreatePaymentLinkRequest(orderCode, (long)payment.Amount, payment.PaymentNumber.Replace("-", "")), cancellationToken);
        }
        catch (PaymentGatewayUnavailableException)
        {
            await UpdateAsync(payment.Id, p => p.MarkFailed(clock.UtcNow), cancellationToken);
            throw;
        }

        var metadata = JsonSerializer.Serialize(new { qrCode = link.QrCode, expiresAt = link.ExpiresAt });
        await UpdateAsync(payment.Id, p => p.SetProviderLink(Provider, orderCode, link.PaymentLinkId, link.CheckoutUrl, metadata),
            cancellationToken);

        return new PayOsPaymentResponse(payment.Id, payment.PaymentNumber, payment.Amount, link.CheckoutUrl, link.QrCode, orderCode,
            link.ExpiresAt, EnumText.Format(PaymentStatus.Pending));
    }

    // A Farmer cancels their own PENDING payOS payment: at payOS first, then here. A link payOS already reports PAID is
    // applied instead (nothing is cancelled).
    public async Task<PaymentResponse> CancelMineAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var me = (await currentFarmer.GetAsync(cancellationToken)).FarmerProfileId;
        var payment = await FindPayOsAsync(paymentId, me, cancellationToken);
        if (payment.Status != PaymentStatus.Pending)
        {
            throw new BusinessRuleException($"Payment '{payment.PaymentNumber}' is {EnumText.Format(payment.Status)}; only a pending payment can be cancelled.");
        }

        var state = await gateway.GetPaymentLinkAsync(payment.ProviderOrderCode!.Value, cancellationToken);
        if (state.Status is PaymentLinkStatus.Pending or PaymentLinkStatus.Processing or PaymentLinkStatus.Underpaid)
        {
            state = await gateway.CancelPaymentLinkAsync(payment.ProviderOrderCode.Value, "Cancelled by the customer.", cancellationToken);
        }

        await ApplyStateAsync(paymentId, state, cancellationToken);

        return await queries.GetAsync(paymentId, me, cancellationToken);
    }

    // Operate, or a Farmer for their own payment. PAID → as a webhook (C-D8); EXPIRED/FAILED → FAILED; CANCELLED → CANCELLED;
    // PENDING/PROCESSING/UNDERPAID → unchanged.
    public async Task<PaymentResponse> SyncAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var me = await FarmerOrNullAsync(cancellationToken);
        var payment = await FindPayOsAsync(paymentId, me, cancellationToken);
        if (payment.Status == PaymentStatus.Pending)
        {
            var state = await gateway.GetPaymentLinkAsync(payment.ProviderOrderCode!.Value, cancellationToken);
            await ApplyStateAsync(paymentId, state, cancellationToken);
        }

        return await queries.GetAsync(paymentId, me, cancellationToken);
    }

    // Operate, or a Farmer for their own payment. Only when the API runs with the simulated gateway (PayOS:Mode=Simulated, a
    // development setting); otherwise the feature does not exist (404). The link is marked paid in the simulator and the payment
    // is then settled through SyncAsync, the same path a real PAID answer from payOS takes. Idempotent: an already PAID payment
    // is returned as it is.
    public async Task<PaymentResponse> SimulatePaidAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var gateway = simulator ?? throw new NotFoundException("Simulated payment", paymentId);
        var me = await FarmerOrNullAsync(cancellationToken);
        var payment = await FindPayOsAsync(paymentId, me, cancellationToken);
        if (payment.Status == PaymentStatus.Paid)
        {
            return await queries.GetAsync(paymentId, me, cancellationToken);
        }

        if (payment.Status != PaymentStatus.Pending)
        {
            throw new BusinessRuleException($"Payment '{payment.PaymentNumber}' is {EnumText.Format(payment.Status)}; only a pending payment can be paid.");
        }

        if (!gateway.TryMarkPaid(payment.ProviderOrderCode!.Value))
        {
            throw new BusinessRuleException(
                $"Payment '{payment.PaymentNumber}' has no live simulated link (the API restarted since it was created): start the payment again.");
        }

        return await SyncAsync(paymentId, cancellationToken);
    }

    // Internal Application entry point for the scheduled reconciler. It is not exposed by the HTTP service contract.
    internal async Task SyncSystemAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        if (currentUser.IsAuthenticated) throw new ForbiddenException();
        var payment = await context.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == paymentId
            && p.PaymentMethod == PaymentMethod.PayOs, cancellationToken);
        if (payment?.Status == PaymentStatus.Pending && payment.ProviderOrderCode is { } code)
        {
            var state = await gateway.GetPaymentLinkAsync(code, cancellationToken);
            await ApplyStateAsync(paymentId, state, cancellationToken);
        }
    }

    // FLOW_2 §6.3: one transaction, idempotent. Business rejections after a valid signature never surface as 4xx (payOS
    // would retry them forever); only an unexpected failure escapes (500, payOS retries).
    public async Task<PayOsWebhookResult> HandleWebhookAsync(string rawPayload, CancellationToken cancellationToken)
    {
        var verification = await gateway.VerifyWebhookAsync(rawPayload, cancellationToken);
        if (verification.Kind == PaymentWebhookKind.NoData)
        {
            return new PayOsWebhookResult(PayOsWebhookOutcome.Registration);
        }

        if (verification.Kind == PaymentWebhookKind.Invalid || verification.Data is null)
        {
            return new PayOsWebhookResult(PayOsWebhookOutcome.BadSignature);
        }

        var data = verification.Data;
        var target = await context.Payments.AsNoTracking().IgnoreQueryFilters()
            .Where(p => p.ProviderOrderCode == data.OrderCode && p.PaymentMethod == PaymentMethod.PayOs)
            .Select(p => new { p.Id, p.OrderId })
            .FirstOrDefaultAsync(cancellationToken);
        if (target is null)
        {
            return new PayOsWebhookResult(PayOsWebhookOutcome.UnknownOrderCode, data.OrderCode);
        }

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        var payment = await LockAndLoadAsync(target.Id, target.OrderId, cancellationToken);
        try
        {
            var outcome = data.IsPaid
                ? await SettleAsync(payment, PaymentLinkStatus.Paid, data.Amount, data.Reference, data.RawData, cancellationToken)
                : await SettleAsync(payment, PaymentLinkStatus.Failed, 0, null, null, cancellationToken);

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new PayOsWebhookResult(outcome, data.OrderCode);
        }
        catch (Exception exception) when (exception is BusinessRuleException or DomainException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new PayOsWebhookResult(PayOsWebhookOutcome.AllocationRefused, data.OrderCode);
        }
    }

    // Applies a payOS link state to a locked, tracked payment inside the caller's transaction; never saves.
    private async Task<PayOsWebhookOutcome> SettleAsync(
        Payment payment, PaymentLinkStatus status, long paidAmount, string? reference, string? metadata, CancellationToken cancellationToken)
    {
        if (payment.Status == PaymentStatus.Paid)
        {
            return PayOsWebhookOutcome.AlreadyPaid;
        }

        var now = clock.UtcNow;
        if (status == PaymentLinkStatus.Paid)
        {
            if (payment.Status != PaymentStatus.Pending)
            {
                return PayOsWebhookOutcome.PaidAfterClose;
            }

            // An overpayment is not accepted as paid either: staff follow it up.
            if (paidAmount != payment.Amount)
            {
                return PayOsWebhookOutcome.AmountMismatch;
            }

            payment.MarkPaid(PaymentConfirmationSource.PayOsWebhook, now, null, reference, metadata);
            var order = payment.OrderId is { } orderId ? await context.Orders.FirstAsync(o => o.Id == orderId, cancellationToken) : null;
            await allocator.AllocateAsync(payment, order, null, payment.CreatedBy!.Value, cancellationToken);
            audit.Record("PAYMENT_RECEIVED", "PAYMENT", payment.Id, payment.StoreId, null,
                new { payment.PaymentNumber, method = "PAYOS", payment.Amount, payment.OrderId, payment.ProviderOrderCode, reference });

            return PayOsWebhookOutcome.Paid;
        }

        if (payment.Status != PaymentStatus.Pending)
        {
            return PayOsWebhookOutcome.NotPaid;
        }

        switch (status)
        {
            case PaymentLinkStatus.Cancelled:
                payment.Cancel(now);
                audit.Record("PAYMENT_CANCELLED", "PAYMENT", payment.Id, payment.StoreId, null, new { payment.PaymentNumber });
                break;
            case PaymentLinkStatus.Expired or PaymentLinkStatus.Failed:
                payment.MarkFailed(now);
                audit.Record("PAYMENT_FAILED", "PAYMENT", payment.Id, payment.StoreId, null,
                    new { payment.PaymentNumber, linkStatus = EnumText.Format(status) });
                break;
        }

        return PayOsWebhookOutcome.NotPaid;
    }

    // Status query result → payment, in its own short transaction.
    private async Task ApplyStateAsync(Guid paymentId, PaymentLinkState state, CancellationToken cancellationToken)
    {
        var orderId = await context.Payments.AsNoTracking().Where(p => p.Id == paymentId).Select(p => p.OrderId).FirstAsync(cancellationToken);

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        var payment = await LockAndLoadAsync(paymentId, orderId, cancellationToken);
        var metadata = JsonSerializer.Serialize(new { confirmedVia = "STATUS_QUERY", state.OrderCode, state.Amount, state.AmountPaid });
        if (payment.ProviderOrderCode != state.OrderCode || payment.Amount != state.Amount)
            throw new BusinessRuleException("The payment gateway response does not match the payment.");
        await SettleAsync(payment, state.Status, state.AmountPaid, null, metadata, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // Closes the PENDING payOS links of an order before a new one is made. A link payOS reports PAID is applied and the
    // new request refused (409): the order's balance changed.
    private async Task CloseOpenLinksAsync(Guid orderId, Guid? me, Guid storeId, CancellationToken cancellationToken)
    {
        if (!await context.Orders.AsNoTracking()
                .AnyAsync(o => o.Id == orderId && o.StoreId == storeId && (me == null || o.FarmerProfileId == me), cancellationToken))
        {
            throw new NotFoundException("Order", orderId);
        }

        var open = await context.Payments.AsNoTracking()
            .Where(p => p.OrderId == orderId && p.PaymentMethod == PaymentMethod.PayOs && p.Status == PaymentStatus.Pending
                && p.ProviderOrderCode != null)
            .Select(p => new { p.Id, Code = p.ProviderOrderCode!.Value })
            .ToListAsync(cancellationToken);
        foreach (var link in open)
        {
            var state = await gateway.GetPaymentLinkAsync(link.Code, cancellationToken);
            if (state.Status == PaymentLinkStatus.Paid)
            {
                await ApplyStateAsync(link.Id, state, cancellationToken);
                throw new ConflictException("An earlier online payment for this order has just been confirmed; check the order before paying again.");
            }

            if (state.Status is PaymentLinkStatus.Pending or PaymentLinkStatus.Processing or PaymentLinkStatus.Underpaid)
            {
                state = await gateway.CancelPaymentLinkAsync(link.Code, "Replaced by a new payment link.", cancellationToken);
            }

            await ApplyStateAsync(link.Id, state, cancellationToken);
        }
    }

    private async Task<Payment> NewOrderPaymentAsync(
        PayOsPaymentRequest request, Guid? me, Guid storeId, Guid actorId, CancellationToken cancellationToken)
    {
        var orderId = request.OrderId!.Value;
        await locks.LockOrderAsync(orderId, cancellationToken);
        var order = await context.Orders
                .FirstOrDefaultAsync(o => o.Id == orderId && o.StoreId == storeId && (me == null || o.FarmerProfileId == me), cancellationToken)
            ?? throw new NotFoundException("Order", orderId);
        if (order.Status is OrderStatus.Cancelled or OrderStatus.PartiallyCancelled or OrderStatus.Completed)
        {
            throw new BusinessRuleException($"Order '{order.OrderNumber}' is {EnumText.Format(order.Status)}; it cannot take a payment.");
        }

        var remaining = order.TotalAmount - await ledger.GetPaidAmountAsync(order.Id, cancellationToken);
        var amount = request.Amount ?? remaining;
        if (amount <= 0)
        {
            throw new BusinessRuleException($"Order '{order.OrderNumber}' has nothing left to pay.");
        }

        if (amount > remaining)
        {
            throw new BusinessRuleException(
                $"Order '{order.OrderNumber}' has {Math.Max(0m, remaining):0.##} left to pay; the payment is {amount:0.##}.");
        }

        return await NewPaymentAsync(storeId, PaymentContext.OrderPayment, amount, order.FarmerProfileId, order.Id, actorId, cancellationToken);
    }

    private async Task<Payment> NewDebtPaymentAsync(
        PayOsPaymentRequest request, Guid? me, Guid storeId, Guid actorId, CancellationToken cancellationToken)
    {
        var farmerId = me ?? request.FarmerProfileId ?? throw new BusinessRuleException("A DEBT_REPAYMENT needs a farmerProfileId.");
        if (!await context.FarmerProfiles.AsNoTracking().AnyAsync(f => f.Id == farmerId, cancellationToken))
        {
            throw new NotFoundException("Farmer profile", farmerId);
        }

        var balance = await context.DebtAccounts.AsNoTracking()
            .Where(a => a.StoreId == storeId && a.FarmerProfileId == farmerId)
            .Select(a => a.CurrentBalance).FirstOrDefaultAsync(cancellationToken);
        var amount = request.Amount!.Value;
        if (amount > balance)
        {
            throw new BusinessRuleException($"The customer owes {balance:0.##}; the payment is {amount:0.##}.");
        }

        return await NewPaymentAsync(storeId, PaymentContext.DebtRepayment, amount, farmerId, null, actorId, cancellationToken);
    }

    private async Task<Payment> NewPaymentAsync(
        Guid storeId, PaymentContext paymentContext, decimal amount, Guid? payer, Guid? orderId, Guid actorId,
        CancellationToken cancellationToken)
    {
        // payOS amounts are whole VND (decision C-D9): the fraction is paid in cash.
        if (amount != decimal.Truncate(amount))
        {
            throw new BusinessRuleException($"payOS takes whole VND only; pay {amount - decimal.Truncate(amount):0.##} in cash and {decimal.Truncate(amount):0} online.");
        }

        var now = clock.UtcNow;
        var number = await DocumentNumbers.NextAsync(
            context.Payments.IgnoreQueryFilters().AsNoTracking().Where(p => p.StoreId == storeId).Select(p => p.PaymentNumber),
            DocumentNumbers.Payment, BusinessCalendar.Today(now), cancellationToken);

        return new Payment(storeId, number, paymentContext, PaymentMethod.PayOs, amount, now, payer, actorId, orderId: orderId);
    }

    // Positive, unique per merchant and below 2^53 (the SDK limit): milliseconds × 1000 + a random suffix (~1.8e15).
    private async Task<long> NewOrderCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = clock.UtcNow.ToUnixTimeMilliseconds() * 1000 + Random.Shared.Next(1000);
            if (!await context.Payments.IgnoreQueryFilters().AsNoTracking().AnyAsync(p => p.ProviderOrderCode == code, cancellationToken))
            {
                return code;
            }
        }

        throw new ConflictException("Could not reserve a payment code; try again.");
    }

    private async Task UpdateAsync(Guid paymentId, Action<Payment> change, CancellationToken cancellationToken)
    {
        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        await locks.LockPaymentAsync(paymentId, cancellationToken);
        var payment = await context.Payments.FirstAsync(p => p.Id == paymentId, cancellationToken);
        if (payment.Status == PaymentStatus.Pending)
        {
            change(payment);
            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Payment> LockAndLoadAsync(Guid paymentId, Guid? orderId, CancellationToken cancellationToken)
    {
        if (orderId is { } order)
        {
            await locks.LockOrderAsync(order, cancellationToken);
        }

        await locks.LockPaymentAsync(paymentId, cancellationToken);

        return await context.Payments.IgnoreQueryFilters().Include(p => p.Allocations).FirstAsync(p => p.Id == paymentId, cancellationToken);
    }

    // A payOS payment of the store; a Farmer only reaches their own (others → 404).
    private async Task<Payment> FindPayOsAsync(Guid paymentId, Guid? me, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);

        return await context.Payments.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == paymentId && p.StoreId == storeId && p.PaymentMethod == PaymentMethod.PayOs
                    && p.ProviderOrderCode != null && (me == null || p.PayerFarmerProfileId == me), cancellationToken)
            ?? throw new NotFoundException("Payment", paymentId);
    }

    private async Task<Guid?> FarmerOrNullAsync(CancellationToken cancellationToken) =>
        RoleCodeFormat.TryParse(currentUser.Role, out var role) && role == RoleCode.Farmer
            ? (await currentFarmer.GetAsync(cancellationToken)).FarmerProfileId
            : null;

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (databaseErrors.IsUniqueViolation(exception))
        {
            throw new ConflictException("Another payment was created at the same time; try again.");
        }
    }
}
