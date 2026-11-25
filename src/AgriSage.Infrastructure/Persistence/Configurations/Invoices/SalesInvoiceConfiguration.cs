using AgriSage.Domain.Features.Invoices.Entities;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Invoices;

internal sealed class SalesInvoiceConfiguration : EntityConfiguration<SalesInvoice>
{
    protected override string TableName => "sales_invoices";
    protected override void ConfigureEntity(EntityTypeBuilder<SalesInvoice> b)
    {
        b.Property(x => x.InvoiceNumber).HasMaxLength(50); b.Property(x => x.OrderNumber).HasMaxLength(50);
        b.Property(x => x.CustomerName).HasMaxLength(150); b.Property(x => x.CustomerEmail).HasMaxLength(255);
        b.Property(x => x.TotalAmount).IsMoney(); b.Property(x => x.SnapshotJson).HasColumnType("jsonb");
        b.Property(x => x.PdfContent).HasColumnType("bytea");
        b.HasReference<SalesInvoice, Store>(x => x.StoreId); b.HasReference<SalesInvoice, Order>(x => x.OrderId);
        b.HasUserReference(x => x.CreatedBy);
        b.HasIndex(x => x.OrderId).IsUnique(); b.HasIndex(x => new { x.StoreId, x.InvoiceNumber }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.IssuedAt }).IsDescending(false, true);
        b.HasCheck("total_amount", "total_amount >= 0"); b.HasCheck("pdf_content", "octet_length(pdf_content) > 0");
        b.HasCheck("snapshot_json", "jsonb_typeof(snapshot_json) = 'object'");
    }
}
