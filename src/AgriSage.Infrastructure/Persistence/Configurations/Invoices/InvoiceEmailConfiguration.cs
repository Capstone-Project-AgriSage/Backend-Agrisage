using AgriSage.Domain.Features.Invoices.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Invoices;

internal sealed class InvoiceEmailConfiguration : EntityConfiguration<InvoiceEmail>
{
    protected override string TableName => "invoice_emails";
    protected override void ConfigureEntity(EntityTypeBuilder<InvoiceEmail> b)
    {
        b.Property(x => x.Recipient).HasMaxLength(255); b.Property(x => x.Status).HasMaxLength(20); b.Property(x => x.Error).HasMaxLength(500);
        b.HasReference<InvoiceEmail, SalesInvoice>(x => x.InvoiceId); b.HasUserReference(x => x.CreatedBy);
        b.HasIndex(x => new { x.InvoiceId, x.RequestId }).IsUnique(); b.HasIndex(x => new { x.InvoiceId, x.CreatedAt }).IsDescending(false, true);
        b.HasCheck("status", "status IN ('SENDING','SENT','FAILED','UNKNOWN')");
        b.HasCheck("sent_at", "(status = 'SENT' AND sent_at IS NOT NULL) OR (status <> 'SENT' AND sent_at IS NULL)");
    }
}
