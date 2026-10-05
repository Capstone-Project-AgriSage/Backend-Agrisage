using AgriSage.Application.Common;
using AgriSage.Application.Common.Validators;
using AgriSage.Domain.Features.Returns.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Returns;

public sealed class ReturnItemRequestValidator : AbstractValidator<ReturnItemRequest>
{
    public ReturnItemRequestValidator()
    {
        RuleFor(r => r.OrderItemId).NotEmpty();
        RuleFor(r => r.ReturnedBaseQuantity).GreaterThan(0);
        RuleFor(r => r.DeliveryItemLotAllocationId).NotEqual(Guid.Empty);
        RuleFor(r => r.OriginalStockMovementItemId).NotEqual(Guid.Empty);
        RuleFor(r => r).Must(r => (r.DeliveryItemLotAllocationId is null) != (r.OriginalStockMovementItemId is null))
            .WithMessage("Exactly one fulfillment source is required.");
        RuleFor(r => r.ReasonCode).Must(v => v?.Trim().ToUpperInvariant() is
            "WRONG_PRODUCT" or "DAMAGED_PRODUCT" or "QUALITY_ISSUE" or "EXPIRED_PRODUCT" or
            "DELIVERY_DAMAGE" or "CUSTOMER_REJECTION" or "OTHER").WithMessage("Invalid return reason.");
    }
}
public sealed class CreateReturnRequestValidator : AbstractValidator<CreateReturnRequest>
{
    public CreateReturnRequestValidator()
    {
        RuleFor(r => r.OrderId).NotEmpty();
        RuleFor(r => r.Note).MaximumLength(1000);
        RuleFor(r => r.ReasonSummary).MaximumLength(1000);
        RuleFor(r => r.Items).NotEmpty();
        RuleForEach(r => r.Items).NotNull().SetValidator(new ReturnItemRequestValidator());
    }
}
public sealed class ReturnReasonRequestValidator : AbstractValidator<ReturnReasonRequest>
{
    public ReturnReasonRequestValidator() => RuleFor(r => r.Reason).NotEmpty().MaximumLength(1000);
}
public sealed class ReturnInspectionRequestValidator : AbstractValidator<ReturnInspectionRequest>
{
    public ReturnInspectionRequestValidator()
    {
        RuleFor(r => r.ConditionStatus).Must(v => EnumText.TryParse<ReturnConditionStatus>(v, out var status)
            && status != ReturnConditionStatus.PendingInspection).WithMessage("Condition must be RESELLABLE, DAMAGED, EXPIRED or UNUSABLE.");
        RuleFor(r => r.InspectionNote).MaximumLength(1000);
    }
}
public sealed class SalesReturnListRequestValidator : AbstractValidator<SalesReturnListRequest>
{
    public SalesReturnListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Status).Must(v => EnumText.TryParse<SalesReturnStatus>(v, out _)).When(r => !string.IsNullOrWhiteSpace(r.Status));
        RuleFor(r => r.OrderId).NotEqual(Guid.Empty);
        RuleFor(r => r.FarmerProfileId).NotEqual(Guid.Empty);
        RuleFor(r => r.Search).MaximumLength(100);
        RuleFor(r => r.FromDate).GreaterThan(DateOnly.MinValue);
        RuleFor(r => r.ToDate).GreaterThan(DateOnly.MinValue);
        RuleFor(r => r.ToDate).LessThan(DateOnly.MaxValue);
        RuleFor(r => r.ToDate).GreaterThanOrEqualTo(r => r.FromDate).When(r => r.FromDate is not null && r.ToDate is not null);
    }
}
