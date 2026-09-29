using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Returns.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Returns;

// Table 52: sales_returns. Aggregate root for sales_return_items and refunds.
internal sealed class SalesReturnConfiguration : EntityConfiguration<SalesReturn>
{
    protected override string TableName => "sales_returns";

    protected override void ConfigureEntity(EntityTypeBuilder<SalesReturn> builder)
    {
        builder.Property(salesReturn => salesReturn.ReturnNumber).HasMaxLength(50);
        builder.Property(salesReturn => salesReturn.Status).HasMaxLength(30);
        builder.Property(salesReturn => salesReturn.ReasonSummary).HasMaxLength(1000);
        builder.Property(salesReturn => salesReturn.CancelReason).HasMaxLength(1000);
        builder.Property(salesReturn => salesReturn.TotalReturnAmount).IsMoney().HasDbDefault(0m);
        builder.Property(salesReturn => salesReturn.TotalRefundAmount).IsMoney().HasDbDefault(0m);
        builder.Property(salesReturn => salesReturn.TotalDebtAdjustment).IsMoney().HasDbDefault(0m);
        builder.Property(salesReturn => salesReturn.Note).HasMaxLength(1000);

        builder.HasReference<SalesReturn, Store>(salesReturn => salesReturn.StoreId);
        builder.HasReference<SalesReturn, Order>(salesReturn => salesReturn.OrderId);
        builder.HasOne(salesReturn => salesReturn.FarmerProfile).WithMany().HasForeignKey(salesReturn => salesReturn.FarmerProfileId);
        builder.HasUserReference(salesReturn => salesReturn.RequestedBy);
        builder.HasUserReference(salesReturn => salesReturn.ApprovedBy);
        builder.HasUserReference(salesReturn => salesReturn.ReceivedBy);
        builder.HasUserReference(salesReturn => salesReturn.InspectedBy);
        builder.HasUserReference(salesReturn => salesReturn.CancelledBy);
        builder.HasMany(salesReturn => salesReturn.Items).WithOne().HasForeignKey(item => item.SalesReturnId);
        builder.HasMany(salesReturn => salesReturn.Refunds).WithOne().HasForeignKey(refund => refund.SalesReturnId);

        builder.HasIndex(salesReturn => new { salesReturn.StoreId, salesReturn.ReturnNumber }).IsUnique();
        builder.HasIndex(salesReturn => new { salesReturn.OrderId, salesReturn.RequestedAt }).IsDescending(false, true);
        builder.HasIndex(salesReturn => new { salesReturn.Status, salesReturn.RequestedAt }).IsDescending(false, true);
        builder.HasIndex(salesReturn => new { salesReturn.FarmerProfileId, salesReturn.RequestedAt }).IsDescending(false, true);

        builder.HasCheck("total_return_amount", "total_return_amount >= 0");
        builder.HasCheck("total_refund_amount", "total_refund_amount >= 0");
        builder.HasCheck("total_debt_adjustment", "total_debt_adjustment >= 0");
    }
}
