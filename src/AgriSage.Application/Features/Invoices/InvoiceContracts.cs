using AgriSage.Application.Common.Models;
using AgriSage.Application.Common.Validators;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using FluentValidation;

namespace AgriSage.Application.Features.Invoices;

public sealed record InvoiceStore(string Name, string Address, string? Phone, string? Email, string? TaxCode);
public sealed record InvoiceSnapshot(int Version, InvoiceStore Store, string CashierName, OrderResponse Order, OrderPaymentSummary Payments);
public sealed record InvoiceListItem(Guid Id, string InvoiceNumber, Guid OrderId, string OrderNumber, string CustomerName,
    string? CustomerEmail, decimal TotalAmount, DateTimeOffset IssuedAt, string EmailStatus = "NOT_SENT");
public sealed record InvoiceResponse(Guid Id, string InvoiceNumber, Guid OrderId, string OrderNumber, string CustomerName,
    string? CustomerEmail, decimal TotalAmount, DateTimeOffset IssuedAt, InvoiceSnapshot Snapshot);
public sealed record InvoiceIssueResult(InvoiceResponse Invoice, bool Created);
public sealed record InvoicePdf(string FileName, byte[] Content);
public sealed record InvoiceListRequest : PaginationRequest
{
    public string? Search { get; init; }
    public Guid? OrderId { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public decimal? MinAmount { get; init; }
    public decimal? MaxAmount { get; init; }
    public bool? HasCustomerEmail { get; init; }
    public string? EmailStatus { get; init; }
}
public sealed record InvoiceEmailRequest(string Recipient, Guid RequestId);
public sealed record InvoiceEmailResponse(Guid Id, string Recipient, string Status, DateTimeOffset CreatedAt, DateTimeOffset? SentAt, string? Error);

public interface IInvoiceService
{
    Task<InvoiceIssueResult> IssueAsync(Guid orderId, CancellationToken token);
    Task<PagedResult<InvoiceListItem>> ListAsync(InvoiceListRequest request, CancellationToken token);
    Task<InvoiceResponse> GetAsync(Guid id, CancellationToken token);
    Task<InvoicePdf> PdfAsync(Guid id, CancellationToken token);
    Task<InvoiceEmailResponse> SendEmailAsync(Guid id, InvoiceEmailRequest request, CancellationToken token);
    Task<PagedResult<InvoiceEmailResponse>> EmailsAsync(Guid id, PaginationRequest request, CancellationToken token);
}

public interface IInvoicePdfRenderer { byte[] Render(string invoiceNumber, DateTimeOffset issuedAt, InvoiceSnapshot snapshot); }
public interface IInvoiceEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(string recipient, string invoiceNumber, string customerName, byte[] pdf, CancellationToken token);
}
public sealed class InvoiceEmailUnavailableException : Exception
{
    public InvoiceEmailUnavailableException() : base("Dịch vụ gửi hóa đơn qua email chưa sẵn sàng.") { }
}
public sealed class InvoiceListValidator : AbstractValidator<InvoiceListRequest>
{
    public InvoiceListValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.OrderId).NotEqual(Guid.Empty);
        RuleFor(x => x.FromDate).Must(ValidDate).WithMessage("Ngày lập phải từ 0001-01-02 đến 9999-12-30.");
        RuleFor(x => x.ToDate).Must(ValidDate).WithMessage("Ngày lập phải từ 0001-01-02 đến 9999-12-30.");
        RuleFor(x => x.ToDate).Must((request, to) => !to.HasValue || !request.FromDate.HasValue || to >= request.FromDate)
            .WithMessage("Ngày đến không được trước ngày từ.");
        RuleFor(x => x.MinAmount).Must(ValidAmount).WithMessage("Số tiền phải không âm và có tối đa hai chữ số thập phân.");
        RuleFor(x => x.MaxAmount).Must(ValidAmount).WithMessage("Số tiền phải không âm và có tối đa hai chữ số thập phân.");
        RuleFor(x => x.MaxAmount).Must((request, max) => !max.HasValue || !request.MinAmount.HasValue || max >= request.MinAmount)
            .WithMessage("Số tiền tối đa không được nhỏ hơn số tiền tối thiểu.");
        RuleFor(x => x.EmailStatus).Must(status => string.IsNullOrWhiteSpace(status) ||
            new[] { "NOT_SENT", "SENDING", "SENT", "FAILED", "UNKNOWN" }.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Trạng thái gửi email không hợp lệ.");
    }

    private static bool ValidDate(DateOnly? date) => date is null || date > DateOnly.MinValue && date < DateOnly.MaxValue;
    private static bool ValidAmount(decimal? amount) => amount is null || amount >= 0 && amount <= 9999999999999999.99m && decimal.Round(amount.Value, 2) == amount;
}
public sealed class InvoiceEmailValidator : AbstractValidator<InvoiceEmailRequest>
{
    public InvoiceEmailValidator()
    {
        RuleFor(x => x.Recipient).NotEmpty().MaximumLength(255).EmailAddress()
            .Must(value => value is not null && !value.Contains('\r') && !value.Contains('\n') && System.Net.Mail.MailAddress.TryCreate(value, out var parsed) && parsed.Address == value);
        RuleFor(x => x.RequestId).NotEmpty();
    }
}
