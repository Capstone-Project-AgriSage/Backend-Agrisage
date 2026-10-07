using AgriSage.Application.Common;
using AgriSage.Domain.Features.Payments.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Payments;

// FLOW_2 §6 (task F2.4): payOS payments. PaymentContext ORDER_PAYMENT (orderId; amount defaults to what is left to pay) or
// DEBT_REPAYMENT (amount required, allocated oldest due date first when PAID, decision C-D1). FarmerProfileId: staff
// debt repayments only; a Farmer always pays for themselves.
public sealed record PayOsPaymentRequest(
    string PaymentContext,
    Guid? OrderId = null,
    decimal? Amount = null,
    Guid? FarmerProfileId = null);

// QrCode = VietQR payload string (the client renders it as an image).
public sealed record PayOsPaymentResponse(
    Guid PaymentId,
    string PaymentNumber,
    decimal Amount,
    string CheckoutUrl,
    string? QrCode,
    long ProviderOrderCode,
    DateTimeOffset? ExpiresAt,
    string Status);

public enum PayOsWebhookOutcome
{
    // No data/signature: registration test or health check.
    Registration,
    BadSignature,
    UnknownOrderCode,
    AlreadyPaid,
    AmountMismatch,
    Paid,
    NotPaid,
    // Money received on a payment cancelled or failed locally: manual handling.
    PaidAfterClose,
    // Paid, but allocating it was refused (e.g. no debt to repay yet): the payment stays PENDING for manual handling.
    AllocationRefused
}

public sealed record PayOsWebhookResult(PayOsWebhookOutcome Outcome, long? OrderCode = null);

public interface IPayOsPaymentService
{
    Task<PayOsPaymentResponse> CreateAsync(PayOsPaymentRequest request, CancellationToken cancellationToken);

    Task<PaymentResponse> CancelMineAsync(Guid paymentId, CancellationToken cancellationToken);

    Task<PaymentResponse> SyncAsync(Guid paymentId, CancellationToken cancellationToken);

    // Test environment only (PayOS:Mode=Simulated; 404 otherwise): "pays" the payment's simulated link and applies it like a
    // status query would, so everything after the payment is the real code.
    Task<PaymentResponse> SimulatePaidAsync(Guid paymentId, CancellationToken cancellationToken);

    Task<PayOsWebhookResult> HandleWebhookAsync(string rawPayload, CancellationToken cancellationToken);
}

public sealed class PayOsPaymentRequestValidator : AbstractValidator<PayOsPaymentRequest>
{
    public PayOsPaymentRequestValidator()
    {
        RuleFor(r => r.PaymentContext).Must(c => EnumText.TryParse<PaymentContext>(c, out _))
            .WithMessage("PaymentContext must be ORDER_PAYMENT or DEBT_REPAYMENT.");
        RuleFor(r => r.OrderId).Must(id => id is { } value && value != Guid.Empty)
            .When(r => EnumText.TryParse<PaymentContext>(r.PaymentContext, out var c) && c == PaymentContext.OrderPayment)
            .WithMessage("An ORDER_PAYMENT needs an orderId.");
        RuleFor(r => r.OrderId).Null()
            .When(r => EnumText.TryParse<PaymentContext>(r.PaymentContext, out var c) && c == PaymentContext.DebtRepayment)
            .WithMessage("A DEBT_REPAYMENT does not reference an order.");
        RuleFor(r => r.Amount).NotNull()
            .When(r => EnumText.TryParse<PaymentContext>(r.PaymentContext, out var c) && c == PaymentContext.DebtRepayment)
            .WithMessage("A DEBT_REPAYMENT needs an amount.");
        // Whole VND is a business rule (decision C-D9, 422); here only the money shape.
        RuleFor(r => r.Amount).GreaterThan(0).Must(a => decimal.Round(a!.Value, 2) == a)
            .When(r => r.Amount is not null).WithMessage("amount must be positive with at most 2 decimals.");
        RuleFor(r => r.FarmerProfileId).NotEqual(Guid.Empty);
    }
}
