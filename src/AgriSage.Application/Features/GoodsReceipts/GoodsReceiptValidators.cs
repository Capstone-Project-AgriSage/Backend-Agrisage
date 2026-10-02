using AgriSage.Application.Common;
using AgriSage.Application.Common.Validators;
using AgriSage.Domain.Features.GoodsReceipts.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.GoodsReceipts;

internal static class ReceiptRuleExtensions
{
    // Money has at most 2 decimals and is never rounded (coding rule #61).
    public static IRuleBuilderOptions<T, decimal> MustBeMoney<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.GreaterThanOrEqualTo(0)
            .Must(value => (decimal.GetBits(value)[3] >> 16 & 0xFF) <= 2 || value == decimal.Round(value, 2))
            .WithMessage("An amount can have at most 2 decimal places.");
}

public sealed class GoodsReceiptItemRequestValidator : AbstractValidator<GoodsReceiptItemRequest>
{
    public GoodsReceiptItemRequestValidator()
    {
        RuleFor(r => r.StoreProductId).NotEmpty();
        RuleFor(r => r.ProductPackagingId).NotEmpty();
        RuleFor(r => r.ReceivedQuantity).GreaterThan(0);
        RuleFor(r => r.PurchaseUnitCost).MustBeMoney();
        RuleFor(r => r.SupplierLotNumber).MaximumLength(100);
        RuleFor(r => r.Note).MaximumLength(500);
    }
}

public sealed class UpdateGoodsReceiptItemRequestValidator : AbstractValidator<UpdateGoodsReceiptItemRequest>
{
    public UpdateGoodsReceiptItemRequestValidator()
    {
        RuleFor(r => r.ReceivedQuantity).GreaterThan(0);
        RuleFor(r => r.PurchaseUnitCost).MustBeMoney();
        RuleFor(r => r.SupplierLotNumber).MaximumLength(100);
        RuleFor(r => r.Note).MaximumLength(500);
    }
}

public sealed class CreateGoodsReceiptRequestValidator : AbstractValidator<CreateGoodsReceiptRequest>
{
    public CreateGoodsReceiptRequestValidator()
    {
        RuleFor(r => r.SupplierId).NotEmpty();
        RuleFor(r => r.SupplierInvoiceNumber).MaximumLength(100);
        RuleFor(r => r.Note).MaximumLength(1000);
        RuleForEach(r => r.Items).SetValidator(new GoodsReceiptItemRequestValidator()).When(r => r.Items is not null);
    }
}

public sealed class UpdateGoodsReceiptRequestValidator : AbstractValidator<UpdateGoodsReceiptRequest>
{
    public UpdateGoodsReceiptRequestValidator()
    {
        RuleFor(r => r.SupplierId).NotEmpty();
        RuleFor(r => r.SupplierInvoiceNumber).MaximumLength(100);
        RuleFor(r => r.Note).MaximumLength(1000);
    }
}

public sealed class CancelGoodsReceiptRequestValidator : AbstractValidator<CancelGoodsReceiptRequest>
{
    public CancelGoodsReceiptRequestValidator() => RuleFor(r => r.Reason).MaximumLength(1000);
}

public sealed class GoodsReceiptListRequestValidator : AbstractValidator<GoodsReceiptListRequest>
{
    public GoodsReceiptListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Status).Must(s => EnumText.TryParse<GoodsReceiptStatus>(s, out _))
            .WithMessage("Status must be DRAFT, CONFIRMED or CANCELLED.")
            .When(r => !string.IsNullOrWhiteSpace(r.Status));
        RuleFor(r => r.ToDate).GreaterThanOrEqualTo(r => r.FromDate)
            .When(r => r.FromDate is not null && r.ToDate is not null)
            .WithMessage("ToDate must not be before FromDate.");
        RuleFor(r => r.Search).MaximumLength(100);
    }
}
