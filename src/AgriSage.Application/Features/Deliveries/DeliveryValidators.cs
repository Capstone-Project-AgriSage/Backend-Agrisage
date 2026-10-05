using AgriSage.Application.Common;
using AgriSage.Application.Common.Validators;
using AgriSage.Application.Features.Orders;
using AgriSage.Domain.Features.Deliveries.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Deliveries;

public sealed class CreateDeliveryRequestValidator : AbstractValidator<CreateDeliveryRequest>
{
    public CreateDeliveryRequestValidator()
    {
        RuleFor(r => r.OrderId).NotEmpty();
        RuleFor(r => r.Items).NotEmpty()
            .Must(items => items is null || items.Count <= CreateCounterOrderRequestValidator.MaxItems)
            .WithMessage($"At most {CreateCounterOrderRequestValidator.MaxItems} lines per delivery.")
            .Must(items => items is null || items.Select(i => i.OrderItemId).Distinct().Count() == items.Count)
            .WithMessage("Each order item may appear only once.");
        RuleForEach(r => r.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.OrderItemId).NotEmpty();
            item.RuleFor(i => i.PlannedQuantity).InclusiveBetween(1, OrderItemRequestValidator.MaxQuantity);
        }).When(r => r.Items is not null);
        RuleFor(r => r.DeliveryAddress!).SetValidator(new DeliveryAddressRequestValidator()).When(r => r.DeliveryAddress is not null);
        RuleFor(r => r.Note).MaximumLength(1000);
    }
}

public sealed class AssignDeliveryRequestValidator : AbstractValidator<AssignDeliveryRequest>
{
    public AssignDeliveryRequestValidator() => RuleFor(r => r.AssignedToUserId).NotEmpty();
}

public sealed class ChangeLotsRequestValidator : AbstractValidator<ChangeLotsRequest>
{
    public ChangeLotsRequestValidator()
    {
        RuleFor(r => r.Lots).NotEmpty()
            .Must(lots => lots is null || lots.Select(l => l.InventoryLotId).Distinct().Count() == lots.Count)
            .WithMessage("Each lot may appear only once.");
        RuleForEach(r => r.Lots).ChildRules(lot =>
        {
            lot.RuleFor(l => l.InventoryLotId).NotEmpty();
            lot.RuleFor(l => l.BaseQuantity).GreaterThan(0);
        }).When(r => r.Lots is not null);
    }
}

public sealed class CancelDeliveryRequestValidator : AbstractValidator<CancelDeliveryRequest>
{
    public CancelDeliveryRequestValidator() => RuleFor(r => r.Reason).NotEmpty().MaximumLength(1000);
}

public sealed class DeliveryListRequestValidator : AbstractValidator<DeliveryListRequest>
{
    public DeliveryListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Status).Must(s => EnumText.TryParse<DeliveryStatus>(s, out _)).When(r => r.Status is not null)
            .WithMessage("Unknown delivery status.");
        RuleFor(r => r.ToDate).Must((r, to) => to is null || r.FromDate is null || to >= r.FromDate)
            .WithMessage("toDate must not be before fromDate.");
        RuleFor(r => r.Search).MaximumLength(150);
    }
}

public sealed class StartAttemptRequestValidator : AbstractValidator<StartAttemptRequest>
{
    public StartAttemptRequestValidator()
    {
        RuleFor(r => r.Items!).NotEmpty()
            .Must(items => items.Select(i => i.AllocationId).Distinct().Count() == items.Count)
            .WithMessage("Each lot allocation may appear only once.")
            .When(r => r.Items is not null);
        RuleForEach(r => r.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.AllocationId).NotEmpty();
            item.RuleFor(i => i.AttemptedBaseQuantity).GreaterThan(0);
        }).When(r => r.Items is not null);
    }
}

public sealed class CompleteAttemptRequestValidator : AbstractValidator<CompleteAttemptRequest>
{
    // FLOW_2 §8; stored as text (the design does not enumerate failure reasons).
    public static readonly string[] FailureReasons =
        ["CUSTOMER_ABSENT", "UNREACHABLE", "CUSTOMER_REFUSED", "DAMAGED", "WEATHER", "VEHICLE_ISSUE", "ADDRESS_ISSUE", "OTHER"];

    public CompleteAttemptRequestValidator()
    {
        RuleFor(r => r.Items!)
            .Must(items => items.Select(i => i.AllocationId).Distinct().Count() == items.Count)
            .WithMessage("Each lot allocation may appear only once.")
            .When(r => r.Items is not null);
        RuleForEach(r => r.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.AllocationId).NotEmpty();
            item.RuleFor(i => i.DeliveredBaseQuantity).GreaterThanOrEqualTo(0);
        }).When(r => r.Items is not null);
        RuleFor(r => r.ReceiverName).MaximumLength(150);
        RuleFor(r => r.ProofImageUrl).MaximumLength(1000);
        RuleFor(r => r.Note).MaximumLength(1000);
        RuleFor(r => r.FailureReasonCode).Must(c => FailureReasons.Contains(c!.Trim().ToUpperInvariant()))
            .When(r => !string.IsNullOrWhiteSpace(r.FailureReasonCode))
            .WithMessage("Unknown failure reason code.");

        // Anything delivered needs the receiver and a proof photo; nothing delivered needs a failure reason.
        RuleFor(r => r).Must(r => !string.IsNullOrWhiteSpace(r.ReceiverName) && !string.IsNullOrWhiteSpace(r.ProofImageUrl))
            .When(r => r.Items?.Any(i => i.DeliveredBaseQuantity > 0) == true)
            .WithName("ProofImageUrl").WithMessage("Goods were handed over: receiverName and proofImageUrl are required.");
        RuleFor(r => r).Must(r => !string.IsNullOrWhiteSpace(r.FailureReasonCode))
            .When(r => r.Items?.Any(i => i.DeliveredBaseQuantity > 0) != true)
            .WithName("FailureReasonCode").WithMessage("Nothing was delivered: failureReasonCode is required.");
    }
}

public sealed class CancelAttemptRequestValidator : AbstractValidator<CancelAttemptRequest>
{
    public CancelAttemptRequestValidator() => RuleFor(r => r.Reason).NotEmpty().MaximumLength(1000);
}

public sealed class ReportIncidentRequestValidator : AbstractValidator<ReportIncidentRequest>
{
    public ReportIncidentRequestValidator()
    {
        RuleFor(r => r.IncidentType).Must(t => EnumText.TryParse<DeliveryIncidentType>(t, out _))
            .WithMessage("Unknown incident type.");
        RuleFor(r => r.Description).NotEmpty().MaximumLength(1000);
        RuleFor(r => r.DeliveryAttemptId).NotEqual(Guid.Empty);
        RuleFor(r => r.AllocationId).NotEqual(Guid.Empty);
        RuleFor(r => r.AffectedBaseQuantity).GreaterThan(0).When(r => r.AffectedBaseQuantity is not null);
        RuleFor(r => r.EvidenceImageUrl).MaximumLength(1000);
    }
}

public sealed class ResolveIncidentRequestValidator : AbstractValidator<ResolveIncidentRequest>
{
    public ResolveIncidentRequestValidator()
    {
        RuleFor(r => r.ResolutionType).Must(t => EnumText.TryParse<DeliveryIncidentResolutionType>(t, out _))
            .WithMessage("Unknown resolution type.");
        RuleFor(r => r.ResolutionNote).MaximumLength(1000);
        RuleFor(r => r.RelatedStockMovementId).NotEqual(Guid.Empty);
    }
}
