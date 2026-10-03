using AgriSage.Application.Common;
using AgriSage.Application.Common.Validators;
using AgriSage.Domain.Features.Payments.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Payments;

public sealed class CashPaymentRequestValidator : AbstractValidator<CashPaymentRequest>
{
    public const int MaxDebtAllocations = 100;

    public CashPaymentRequestValidator()
    {
        RuleFor(r => r.PaymentContext).Must(v => EnumText.TryParse<PaymentContext>(v, out _))
            .WithMessage("PaymentContext must be ORDER_PAYMENT or DEBT_REPAYMENT.");
        RuleFor(r => r.Amount).GreaterThan(0).MustBeMoney();
        RuleFor(r => r.Note).MaximumLength(1000);

        When(r => IsContext(r, PaymentContext.OrderPayment), () =>
        {
            RuleFor(r => r.OrderId).Must(id => id is { } value && value != Guid.Empty).WithMessage("An ORDER_PAYMENT needs an orderId.");
            RuleFor(r => r.DebtAllocations).Must(a => a is null || a.Count == 0)
                .WithMessage("An ORDER_PAYMENT has no debt allocations.");
        });

        When(r => IsContext(r, PaymentContext.DebtRepayment), () =>
        {
            RuleFor(r => r.FarmerProfileId).Must(id => id is { } value && value != Guid.Empty).WithMessage("A DEBT_REPAYMENT needs a farmerProfileId.");
            RuleFor(r => r.OrderId).Null().WithMessage("A DEBT_REPAYMENT has no order.");
            RuleFor(r => r.DebtAllocations)
                .Must(a => a is null || a.Count <= MaxDebtAllocations).WithMessage($"At most {MaxDebtAllocations} debt allocations.")
                .Must(a => a is null || a.Select(x => x.DebtEntryId).Distinct().Count() == a.Count)
                .WithMessage("Each debt entry may appear only once.")
                .Must((r, a) => a is null || a.Count == 0 || a.Sum(x => x.Amount) == r.Amount)
                .WithMessage("The debt allocations must add up to the payment amount.");
            RuleForEach(r => r.DebtAllocations).ChildRules(allocation =>
            {
                allocation.RuleFor(a => a.DebtEntryId).NotEmpty();
                allocation.RuleFor(a => a.Amount).GreaterThan(0).MustBeMoney();
            }).When(r => r.DebtAllocations is not null);
        });
    }

    private static bool IsContext(CashPaymentRequest request, PaymentContext expected) =>
        EnumText.TryParse<PaymentContext>(request.PaymentContext, out var context) && context == expected;
}

public sealed class CancelPaymentRequestValidator : AbstractValidator<CancelPaymentRequest>
{
    public CancelPaymentRequestValidator() => RuleFor(r => r.Reason).MaximumLength(500);
}

public sealed class PaymentListRequestValidator : AbstractValidator<PaymentListRequest>
{
    public PaymentListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.PaymentContext).Must(v => EnumText.TryParse<PaymentContext>(v, out _))
            .When(r => !string.IsNullOrWhiteSpace(r.PaymentContext)).WithMessage("PaymentContext must be ORDER_PAYMENT or DEBT_REPAYMENT.");
        RuleFor(r => r.PaymentMethod).Must(v => PaymentText.TryParseMethod(v, out _))
            .When(r => !string.IsNullOrWhiteSpace(r.PaymentMethod)).WithMessage("PaymentMethod must be CASH or PAYOS.");
        RuleFor(r => r.Status).Must(v => EnumText.TryParse<PaymentStatus>(v, out _))
            .When(r => !string.IsNullOrWhiteSpace(r.Status)).WithMessage("Unknown payment status.");
        RuleFor(r => r.ToDate).GreaterThanOrEqualTo(r => r.FromDate!.Value)
            .When(r => r.FromDate is not null && r.ToDate is not null).WithMessage("ToDate must not be before FromDate.");
        RuleFor(r => r.Search).MaximumLength(100);
    }
}

public sealed class MyPaymentListRequestValidator : AbstractValidator<MyPaymentListRequest>
{
    public MyPaymentListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Status).Must(v => EnumText.TryParse<PaymentStatus>(v, out _))
            .When(r => !string.IsNullOrWhiteSpace(r.Status)).WithMessage("Unknown payment status.");
    }
}
