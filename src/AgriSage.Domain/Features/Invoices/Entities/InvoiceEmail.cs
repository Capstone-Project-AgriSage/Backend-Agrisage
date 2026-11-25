using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;

namespace AgriSage.Domain.Features.Invoices.Entities;

public sealed class InvoiceEmail : AuditableEntity
{
    private InvoiceEmail() { }
    public InvoiceEmail(Guid invoiceId, Guid createdBy, Guid requestId, string recipient)
    {
        if (invoiceId == Guid.Empty || createdBy == Guid.Empty || requestId == Guid.Empty)
            throw new ArgumentException("Email references and request id are required.");
        InvoiceId = invoiceId; CreatedBy = createdBy; RequestId = requestId;
        Recipient = Guard.NotNullOrWhiteSpace(recipient); Status = "SENDING";
    }
    public Guid InvoiceId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid RequestId { get; private set; }
    public string Recipient { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public DateTimeOffset? SentAt { get; private set; }
    public string? Error { get; private set; }
    public void Complete(DateTimeOffset sentAt) { EnsureSending(); Status = "SENT"; SentAt = sentAt; }
    public void Fail(bool interrupted = false)
    {
        EnsureSending(); Status = interrupted ? "UNKNOWN" : "FAILED";
        Error = interrupted ? "Việc gửi bị gián đoạn. Hãy kiểm tra hộp thư trước khi gửi lại." : "Không gửi được email. Vui lòng kiểm tra dịch vụ email và thử lại.";
    }
    private void EnsureSending()
    {
        if (Status != "SENDING") throw new DomainException("A completed email attempt cannot be changed.");
    }
}
