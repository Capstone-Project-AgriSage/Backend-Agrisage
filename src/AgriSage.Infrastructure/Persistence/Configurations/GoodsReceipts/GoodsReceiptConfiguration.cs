using AgriSage.Domain.Features.GoodsReceipts.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.GoodsReceipts;

// Table 22: goods_receipts. Aggregate root for goods_receipt_items.
internal sealed class GoodsReceiptConfiguration : EntityConfiguration<GoodsReceipt>
{
    protected override string TableName => "goods_receipts";

    protected override void ConfigureEntity(EntityTypeBuilder<GoodsReceipt> builder)
    {
        builder.Property(receipt => receipt.ReceiptNumber).HasMaxLength(50);
        builder.Property(receipt => receipt.SupplierInvoiceNumber).HasMaxLength(100);
        builder.Property(receipt => receipt.SourceType).HasMaxLength(20);
        builder.Property(receipt => receipt.SourceFileName).HasMaxLength(255);
        builder.Property(receipt => receipt.SourceFileUrl).HasMaxLength(1000);
        builder.Property(receipt => receipt.Status).HasMaxLength(20);
        builder.Property(receipt => receipt.SubtotalAmount).IsMoney().HasDbDefault(0m);
        builder.Property(receipt => receipt.TotalAmount).IsMoney().HasDbDefault(0m);
        builder.Property(receipt => receipt.Note).HasMaxLength(1000);
        builder.Property(receipt => receipt.CancelReason).HasMaxLength(1000);

        builder.HasReference<GoodsReceipt, Store>(receipt => receipt.StoreId);
        builder.HasOne(receipt => receipt.Supplier).WithMany().HasForeignKey(receipt => receipt.SupplierId);
        builder.HasUserReference(receipt => receipt.ReceivedBy);
        builder.HasUserReference(receipt => receipt.ConfirmedBy);
        builder.HasUserReference(receipt => receipt.CancelledBy);
        builder.HasMany(receipt => receipt.Items).WithOne().HasForeignKey(item => item.GoodsReceiptId);

        builder.HasIndex(receipt => new { receipt.StoreId, receipt.ReceiptNumber }).IsUnique();
        builder.HasIndex(receipt => new { receipt.StoreId, receipt.ReceivedAt }).IsDescending(false, true);
        builder.HasIndex(receipt => new { receipt.SupplierId, receipt.ReceivedAt }).IsDescending(false, true);
        builder.HasIndex(receipt => receipt.Status);

        builder.HasCheck("subtotal_amount", "subtotal_amount >= 0");
        builder.HasCheck("total_amount", "total_amount >= 0");
    }
}
