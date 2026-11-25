using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Invoices.Entities;

public sealed class SalesInvoice : AuditableEntity
{
    private SalesInvoice() { }

    public SalesInvoice(Guid storeId, Guid orderId, Guid createdBy, string invoiceNumber, string orderNumber,
        string customerName, string? customerEmail, decimal totalAmount, DateTimeOffset issuedAt,
        string snapshotJson, byte[] pdfContent)
    {
        if (storeId == Guid.Empty || orderId == Guid.Empty || createdBy == Guid.Empty)
            throw new ArgumentException("Invoice references are required.");
        if (pdfContent.Length == 0) throw new ArgumentException("A saved PDF is required.");
        StoreId = storeId; OrderId = orderId; CreatedBy = createdBy;
        InvoiceNumber = Guard.NotNullOrWhiteSpace(invoiceNumber); OrderNumber = Guard.NotNullOrWhiteSpace(orderNumber);
        CustomerName = Guard.NotNullOrWhiteSpace(customerName); CustomerEmail = customerEmail;
        TotalAmount = Guard.NonNegativeMoney(totalAmount); IssuedAt = issuedAt;
        SnapshotJson = Guard.NotNullOrWhiteSpace(snapshotJson); PdfContent = pdfContent.ToArray();
    }

    public Guid StoreId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public string InvoiceNumber { get; private set; } = null!;
    public string OrderNumber { get; private set; } = null!;
    public string CustomerName { get; private set; } = null!;
    public string? CustomerEmail { get; private set; }
    public decimal TotalAmount { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public string SnapshotJson { get; private set; } = null!;
    public byte[] PdfContent { get; private set; } = [];
}
