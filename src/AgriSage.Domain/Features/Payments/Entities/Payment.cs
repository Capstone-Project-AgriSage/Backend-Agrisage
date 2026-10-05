using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Payments.Enums;

namespace AgriSage.Domain.Features.Payments.Entities;

// Incoming payment (cash or payOS) for an Order or for debt repayment; aggregate root for its Allocations.
// An ORDER_PAYMENT names its Order from creation (order_id, database design §35.18), so a PENDING payOS payment
// already knows its target; a DEBT_REPAYMENT has no Order.
// Webhook verification and idempotency are handled by the calling use case; refunds belong to Return/Refund.
public sealed class Payment : SoftDeletableEntity
{
    public const string DefaultCurrency = "VND";

    private readonly List<PaymentAllocation> _allocations = [];

    private Payment()
    {
    }

    public Payment(
        Guid storeId,
        string paymentNumber,
        PaymentContext paymentContext,
        PaymentMethod paymentMethod,
        decimal amount,
        DateTimeOffset initiatedAt,
        Guid? payerFarmerProfileId = null,
        Guid? createdBy = null,
        string? note = null,
        string currency = DefaultCurrency,
        Guid? orderId = null)
    {
        if (Guard.NotNullOrWhiteSpace(currency).Length != 3)
        {
            throw new DomainException("Currency must be a 3-letter code.");
        }

        if (paymentContext == PaymentContext.OrderPayment && (orderId is null || orderId == Guid.Empty))
        {
            throw new DomainException("An ORDER_PAYMENT must name its order.");
        }

        if (paymentContext == PaymentContext.DebtRepayment && orderId is not null)
        {
            throw new DomainException("A DEBT_REPAYMENT cannot reference an order.");
        }

        StoreId = storeId;
        PaymentNumber = Guard.NotNullOrWhiteSpace(paymentNumber);
        PaymentContext = paymentContext;
        PaymentMethod = paymentMethod;
        Amount = Guard.PositiveMoney(amount);
        Currency = currency;
        InitiatedAt = initiatedAt;
        PayerFarmerProfileId = payerFarmerProfileId;
        OrderId = orderId;
        CreatedBy = createdBy;
        Note = note;
        Status = PaymentStatus.Pending;
    }

    public Guid StoreId { get; private set; }

    // ORDER_PAYMENT only: the order this money is for (allocations go to this order only).
    public Guid? OrderId { get; private set; }

    public string PaymentNumber { get; private set; } = null!;

    public Guid? PayerFarmerProfileId { get; private set; }

    public FarmerProfile? PayerFarmerProfile { get; private set; }

    public PaymentContext PaymentContext { get; private set; }

    public PaymentMethod PaymentMethod { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = null!;

    public PaymentStatus Status { get; private set; }

    // Provider name values are not enumerated by the database design.
    public string? Provider { get; private set; }

    public long? ProviderOrderCode { get; private set; }

    public string? ProviderPaymentLinkId { get; private set; }

    public string? ProviderTransactionId { get; private set; }

    public string? CheckoutUrl { get; private set; }

    // Raw provider JSON (jsonb).
    public string? ProviderMetadata { get; private set; }

    public PaymentConfirmationSource? ConfirmationSource { get; private set; }

    public DateTimeOffset InitiatedAt { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public Guid? ConfirmedBy { get; private set; }

    public DateTimeOffset? FailedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public string? Note { get; private set; }

    public IReadOnlyCollection<PaymentAllocation> Allocations => _allocations.AsReadOnly();

    public decimal UnallocatedAmount => Amount - ActiveAllocations.Sum(a => a.AllocatedAmount);

    public void SetBankTransferReference(string? reference)
    {
        EnsureStatus(PaymentStatus.Pending);
        if (PaymentMethod != PaymentMethod.BankTransfer) throw new DomainException("Only a bank receipt has a manual transfer reference.");
        ProviderTransactionId = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
    }

    public void SetProviderLink(
        string provider,
        long providerOrderCode,
        string providerPaymentLinkId,
        string checkoutUrl,
        string? providerMetadata = null)
    {
        EnsureStatus(PaymentStatus.Pending);

        if (PaymentMethod != PaymentMethod.PayOs)
        {
            throw new DomainException("Only a PAYOS payment has a provider payment link.");
        }

        Provider = Guard.NotNullOrWhiteSpace(provider);
        ProviderOrderCode = providerOrderCode;
        ProviderPaymentLinkId = Guard.NotNullOrWhiteSpace(providerPaymentLinkId);
        CheckoutUrl = Guard.NotNullOrWhiteSpace(checkoutUrl);
        ProviderMetadata = providerMetadata;
    }

    // payOS (F2.4): the order code is stored before the link is requested, so a webhook can always find the payment.
    public void AssignProviderOrderCode(string provider, long providerOrderCode)
    {
        EnsureStatus(PaymentStatus.Pending);

        if (PaymentMethod != PaymentMethod.PayOs)
        {
            throw new DomainException("Only a PAYOS payment has a provider order code.");
        }

        if (ProviderOrderCode is not null)
        {
            throw new DomainException("The payment already has a provider order code.");
        }

        Provider = Guard.NotNullOrWhiteSpace(provider);
        ProviderOrderCode = Guard.Positive(providerOrderCode);
    }

    // Cash is confirmed by staff; payOS is confirmed by its webhook (the source of truth).
    public void MarkPaid(
        PaymentConfirmationSource confirmationSource,
        DateTimeOffset confirmedAt,
        Guid? confirmedBy = null,
        string? providerTransactionId = null,
        string? providerMetadata = null)
    {
        EnsureStatus(PaymentStatus.Pending);

        var expectedSource = PaymentMethod == PaymentMethod.PayOs
            ? PaymentConfirmationSource.PayOsWebhook
            : PaymentConfirmationSource.Staff;

        if (confirmationSource != expectedSource)
        {
            throw new DomainException($"A {PaymentMethod} payment must be confirmed by {expectedSource}.");
        }

        if (confirmationSource == PaymentConfirmationSource.Staff && confirmedBy is null)
        {
            throw new DomainException("Staff confirmation requires the confirming staff member.");
        }

        Status = PaymentStatus.Paid;
        ConfirmationSource = confirmationSource;
        ConfirmedAt = confirmedAt;
        ConfirmedBy = confirmedBy;
        ProviderTransactionId = providerTransactionId ?? ProviderTransactionId;
        ProviderMetadata = providerMetadata ?? ProviderMetadata;
    }

    public void MarkFailed(DateTimeOffset failedAt)
    {
        EnsureStatus(PaymentStatus.Pending);
        Status = PaymentStatus.Failed;
        FailedAt = failedAt;
    }

    public void Cancel(DateTimeOffset cancelledAt)
    {
        EnsureStatus(PaymentStatus.Pending);
        Status = PaymentStatus.Cancelled;
        CancelledAt = cancelledAt;
    }

    public PaymentAllocation AllocateToOrder(
        Guid orderId,
        decimal amount,
        DateTimeOffset allocatedAt,
        Guid? allocatedBy = null)
    {
        if (PaymentContext == PaymentContext.OrderPayment && orderId != OrderId)
        {
            throw new DomainException($"Payment '{PaymentNumber}' is for another order.");
        }

        return Allocate(PaymentAllocationType.Order, orderId, amount, allocatedAt, allocatedBy);
    }

    public PaymentAllocation AllocateToDebtEntry(
        Guid debtEntryId,
        decimal amount,
        DateTimeOffset allocatedAt,
        Guid? allocatedBy = null) =>
        Allocate(PaymentAllocationType.Debt, debtEntryId, amount, allocatedAt, allocatedBy);

    // Applies Order prepayment against successful fulfillment.
    public void ConsumePrepayment(Guid allocationId, decimal amount) =>
        GetAllocation(allocationId).ConsumePrepayment(amount);

    // Gives back `amount` of the unconsumed prepayment of an ORDER allocation (§35.21); the whole allocation when that is
    // everything it holds and nothing was consumed. The caller requests the matching refund.
    public void ReleaseUnconsumedPrepayment(
        Guid allocationId, decimal amount, Guid releasedBy, DateTimeOffset releasedAt, string? reason = null) =>
        GetAllocation(allocationId).ReleaseUnconsumed(amount, releasedBy, releasedAt, reason);

    public void ReverseAllocation(Guid allocationId, Guid reversedBy, DateTimeOffset reversedAt, string? reason = null) =>
        GetAllocation(allocationId).Reverse(reversedBy, reversedAt, reason);

    protected override void EnsureCanBeDeleted() => EnsureStatus(PaymentStatus.Pending);

    private IEnumerable<PaymentAllocation> ActiveAllocations => _allocations.Where(a => !a.IsDeleted && a.IsActive);

    // payment_context binds the allocation type (database design §35.7).
    private PaymentAllocation Allocate(
        PaymentAllocationType allocationType,
        Guid targetId,
        decimal amount,
        DateTimeOffset allocatedAt,
        Guid? allocatedBy)
    {
        EnsureStatus(PaymentStatus.Paid);

        var allowedType = PaymentContext == PaymentContext.OrderPayment
            ? PaymentAllocationType.Order
            : PaymentAllocationType.Debt;

        if (allocationType != allowedType)
        {
            throw new DomainException($"A {PaymentContext} payment can only be allocated to {allowedType}.");
        }

        if (Guard.PositiveMoney(amount) > UnallocatedAmount)
        {
            throw new DomainException($"Cannot allocate {amount}; only {UnallocatedAmount} of the payment is unallocated.");
        }

        var allocation = new PaymentAllocation(Id, allocationType, targetId, amount, allocatedAt, allocatedBy);
        _allocations.Add(allocation);

        return allocation;
    }

    private PaymentAllocation GetAllocation(Guid allocationId) =>
        _allocations.SingleOrDefault(a => a.Id == allocationId && !a.IsDeleted)
        ?? throw new DomainException($"Allocation '{allocationId}' was not found on payment '{PaymentNumber}'.");

    private void EnsureStatus(PaymentStatus expected)
    {
        if (Status != expected)
        {
            throw new DomainException($"Payment '{PaymentNumber}' is {Status}; expected {expected}.");
        }
    }
}
