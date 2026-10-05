using AgriSage.Application.Common;
using AgriSage.Domain.Features.Returns.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Returns;

public sealed class RefundRequestValidator : AbstractValidator<RefundRequest>
{
    public RefundRequestValidator()
    {
        RuleFor(r => r.RefundMethod).Must(v => EnumText.TryParse<RefundMethod>(v, out _)).WithMessage("Refund method must be CASH, BANK_TRANSFER or OTHER_EXTERNAL.");
        RuleFor(r => r.Amount).GreaterThan(0).PrecisionScale(18, 2, true);
        RuleFor(r => r.OriginalPaymentId).NotEqual(Guid.Empty);
        RuleFor(r => r.ExternalReference).MaximumLength(200);
        RuleFor(r => r.Note).MaximumLength(1000);
    }
}
public sealed class OrderRefundRequestValidator : AbstractValidator<OrderRefundRequest>
{
    public OrderRefundRequestValidator()
    {
        RuleFor(r => r.OriginalPaymentId).NotEmpty();
        RuleFor(r => r.RefundMethod).Must(v => EnumText.TryParse<RefundMethod>(v, out _)).WithMessage("Refund method must be CASH, BANK_TRANSFER or OTHER_EXTERNAL.");
        RuleFor(r => r.Amount).GreaterThan(0).PrecisionScale(18, 2, true);
        RuleFor(r => r.Note).MaximumLength(1000);
    }
}
public sealed class CompleteRefundRequestValidator : AbstractValidator<CompleteRefundRequest>
{
    public CompleteRefundRequestValidator()
    {
        RuleFor(r => r.ExternalReference).MaximumLength(200);
        RuleFor(r => r.ProofFileUrl).MaximumLength(1000).Must(Texts.IsHttpsUrl).When(r => !string.IsNullOrWhiteSpace(r.ProofFileUrl))
            .WithMessage("Proof must be an absolute https URL.");
        RuleFor(r => r.Note).MaximumLength(1000);
    }
}
public sealed class FailRefundRequestValidator : AbstractValidator<FailRefundRequest>
{
    public FailRefundRequestValidator() => RuleFor(r => r.Note).MaximumLength(1000);
}
public sealed class CancelRefundRequestValidator : AbstractValidator<CancelRefundRequest>
{
    public CancelRefundRequestValidator() => RuleFor(r => r.Reason).NotEmpty().MaximumLength(1000);
}
