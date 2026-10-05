using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Customers;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Payments;

public sealed record BankDebtPaymentRequest(Guid FarmerProfileId, decimal Amount,
    DateTimeOffset? PaymentDate = null, string? Note = null, string? Reference = null);
public sealed record RejectDebtPaymentRequest(string Reason);
public sealed class BankDebtPaymentRequestValidator : AbstractValidator<BankDebtPaymentRequest>
{
    public BankDebtPaymentRequestValidator()
    {
        RuleFor(r => r.FarmerProfileId).NotEmpty();
        RuleFor(r => r.Amount).Must(a => a > 0 && Credit.CreditMoney.Valid(a));
        RuleFor(r => r.Note).MaximumLength(1000);
        RuleFor(r => r.Reference).MaximumLength(150);
    }
}
public sealed class RejectDebtPaymentRequestValidator : AbstractValidator<RejectDebtPaymentRequest>
{
    public RejectDebtPaymentRequestValidator() => RuleFor(r => r.Reason).NotEmpty().MaximumLength(1000);
}
public interface IBankDebtPaymentService
{
    Task<PaymentResponse> RecordAsync(BankDebtPaymentRequest request, CancellationToken token);
    Task<PaymentResponse> ConfirmAsync(Guid id, CancellationToken token);
    Task<PaymentResponse> RejectAsync(Guid id, RejectDebtPaymentRequest request, CancellationToken token);
}

// Manual incoming bank receipts share the existing Payment aggregate and allocation pipeline.
public sealed class BankDebtPaymentService(IAgriSageDbContext db, IRowLockService locks, CustomerWrites access,
    IDateTimeProvider clock, PaymentAllocator allocator, PaymentQueries queries, AuditTrail audit) : IBankDebtPaymentService
{
    public async Task<PaymentResponse> RecordAsync(BankDebtPaymentRequest request, CancellationToken token)
    {
        var actor = access.Actor();
        var store = await ActiveStore.GetIdAsync(db, token);
        var at = request.PaymentDate?.ToUniversalTime() ?? clock.UtcNow;
        if (at > clock.UtcNow) throw new BusinessRuleException("Payment date cannot be in the future.");
        await using var tx = await db.BeginTransactionAsync(token);
        await locks.LockFarmerProfileAsync(request.FarmerProfileId, token);
        await access.FindAsync(request.FarmerProfileId, token);
        var balance = await db.DebtAccounts.Where(a => a.StoreId == store && a.FarmerProfileId == request.FarmerProfileId)
            .Select(a => a.CurrentBalance).FirstOrDefaultAsync(token);
        if (request.Amount <= 0 || request.Amount > balance) throw new BusinessRuleException("Payment must be positive and within current outstanding debt.");
        var number = await DocumentNumbers.NextAsync(db.Payments.IgnoreQueryFilters().AsNoTracking().Where(p => p.StoreId == store)
            .Select(p => p.PaymentNumber), DocumentNumbers.Payment, BusinessCalendar.Today(clock.UtcNow), token);
        var payment = new Payment(store, number, PaymentContext.DebtRepayment, PaymentMethod.BankTransfer,
            request.Amount, at, request.FarmerProfileId, actor, Texts.Clean(request.Note));
        payment.SetBankTransferReference(request.Reference);
        db.Payments.Add(payment);
        audit.Record("DEBT_PAYMENT_RECORDED", "PAYMENT", payment.Id, store,
            newValues: new { payment.Amount, request.Reference, payment.PayerFarmerProfileId, Status = "PENDING" });
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return await queries.GetAsync(payment.Id, null, token);
    }

    public Task<PaymentResponse> ConfirmAsync(Guid id, CancellationToken token) => MutateAsync(id, null, token);
    public Task<PaymentResponse> RejectAsync(Guid id, RejectDebtPaymentRequest request, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new BusinessRuleException("Rejection requires a reason.");
        return MutateAsync(id, request.Reason.Trim(), token);
    }

    private async Task<PaymentResponse> MutateAsync(Guid id, string? rejectReason, CancellationToken token)
    {
        var actor = access.Actor(manage: true);
        var store = await ActiveStore.GetIdAsync(db, token);
        await using var tx = await db.BeginTransactionAsync(token);
        await locks.LockPaymentAsync(id, token);
        var payment = await db.Payments.Include(p => p.Allocations).SingleOrDefaultAsync(p => p.Id == id && p.StoreId == store, token)
            ?? throw new NotFoundException("Payment", id);
        if (payment.PaymentMethod != PaymentMethod.BankTransfer || payment.PaymentContext != PaymentContext.DebtRepayment)
            throw new BusinessRuleException("Only bank debt receipts use manual confirmation or rejection.");
        if (rejectReason == null && payment.Status == PaymentStatus.Paid)
        {
            await tx.CommitAsync(token);
            return await queries.GetAsync(id, null, token);
        }
        if (payment.Status != PaymentStatus.Pending) throw new BusinessRuleException("Only pending payments can be reviewed.");
        if (rejectReason == null)
        {
            await locks.LockFarmerProfileAsync(payment.PayerFarmerProfileId!.Value, token);
            payment.MarkPaid(PaymentConfirmationSource.Staff, clock.UtcNow, actor);
            await allocator.AllocateAsync(payment, null, null, actor, token);
        }
        else payment.MarkFailed(clock.UtcNow);
        audit.Record(rejectReason == null ? "DEBT_PAYMENT_CONFIRMED" : "DEBT_PAYMENT_REJECTED", "PAYMENT", id, store,
            newValues: new { payment.Amount, Status = EnumText.Format(payment.Status) }, reason: rejectReason);
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return await queries.GetAsync(id, null, token);
    }
}
