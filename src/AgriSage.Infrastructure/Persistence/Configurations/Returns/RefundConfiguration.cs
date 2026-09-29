using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Returns.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Returns;

// Table 54: refunds.
internal sealed class RefundConfiguration : EntityConfiguration<Refund>
{
    protected override string TableName => "refunds";

    protected override void ConfigureEntity(EntityTypeBuilder<Refund> builder)
    {
        builder.Property(refund => refund.RefundNumber).HasMaxLength(50);
        builder.Property(refund => refund.RefundMethod).HasMaxLength(30);
        builder.Property(refund => refund.Amount).IsMoney();
        builder.Property(refund => refund.Currency).IsCurrencyCode().HasDbDefault(Refund.DefaultCurrency);
        builder.Property(refund => refund.Status).HasMaxLength(30);
        builder.Property(refund => refund.ExternalReference).HasMaxLength(200);
        builder.Property(refund => refund.ProofFileUrl).HasMaxLength(1000);
        builder.Property(refund => refund.CancelReason).HasMaxLength(1000);
        builder.Property(refund => refund.Note).HasMaxLength(1000);

        builder.Ignore(refund => refund.CountsTowardRefundTotal);

        builder.HasReference<Refund, Store>(refund => refund.StoreId);
        builder.HasReference<Refund, Payment>(refund => refund.OriginalPaymentId);
        builder.HasUserReference(refund => refund.RequestedBy);
        builder.HasUserReference(refund => refund.CompletedBy);
        builder.HasUserReference(refund => refund.CancelledBy);

        builder.HasIndex(refund => new { refund.StoreId, refund.RefundNumber }).IsUnique();
        builder.HasIndex(refund => new { refund.Status, refund.RequestedAt });

        builder.HasCheck("amount", "amount > 0");
    }
}
