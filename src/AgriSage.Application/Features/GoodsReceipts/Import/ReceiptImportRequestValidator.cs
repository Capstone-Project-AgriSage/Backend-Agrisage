using FluentValidation;

namespace AgriSage.Application.Features.GoodsReceipts.Import;

// Header form fields; the file itself is checked by GoodsReceiptImportService (type, size, rows).
public sealed class ReceiptImportRequestValidator : AbstractValidator<ReceiptImportRequest>
{
    public ReceiptImportRequestValidator()
    {
        RuleFor(r => r.SupplierId).NotEmpty();
        RuleFor(r => r.SupplierInvoiceNumber).MaximumLength(100);
        RuleFor(r => r.Note).MaximumLength(1000);
    }
}
