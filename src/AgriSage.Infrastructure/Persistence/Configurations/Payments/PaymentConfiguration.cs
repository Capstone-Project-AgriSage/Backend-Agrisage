using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Payments;

// Table 36: payments. Aggregate root for payment_allocations. order_id: database design §35.18.
internal sealed class PaymentConfiguration : EntityConfiguration<Payment>
{
    protected override string TableName => "payments";

    protected override void ConfigureEntity(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(payment => payment.PaymentNumber).HasMaxLength(50);
        builder.Property(payment => payment.PaymentContext).HasMaxLength(30);
        builder.Property(payment => payment.PaymentMethod).HasMaxLength(20);
        builder.Property(payment => payment.Amount).IsMoney();
        builder.Property(payment => payment.Currency).IsCurrencyCode().HasDbDefault(Payment.DefaultCurrency);
        builder.Property(payment => payment.Status).HasMaxLength(30);
        builder.Property(payment => payment.Provider).HasMaxLength(30);
        builder.Property(payment => payment.ProviderPaymentLinkId).HasMaxLength(150);
        builder.Property(payment => payment.ProviderTransactionId).HasMaxLength(150);
        builder.Property(payment => payment.CheckoutUrl).HasMaxLength(1500);
        builder.Property(payment => payment.ProviderMetadata).IsJson();
        builder.Property(payment => payment.ConfirmationSource).HasMaxLength(30);
        builder.Property(payment => payment.Note).HasMaxLength(1000);

        builder.Ignore(payment => payment.UnallocatedAmount);

        builder.HasReference<Payment, Store>(payment => payment.StoreId);
        builder.HasReference<Payment, Order>(payment => payment.OrderId);
        builder.HasOne(payment => payment.PayerFarmerProfile).WithMany().HasForeignKey(payment => payment.PayerFarmerProfileId);
        builder.HasUserReference(payment => payment.ConfirmedBy);
        builder.HasUserReference(payment => payment.CreatedBy);
        builder.HasMany(payment => payment.Allocations).WithOne().HasForeignKey(allocation => allocation.PaymentId);

        builder.HasIndex(payment => new { payment.StoreId, payment.PaymentNumber }).IsUnique();
        builder.HasIndex(payment => payment.ProviderOrderCode)
            .IsUnique()
            .HasFilter("provider_order_code IS NOT NULL");
        builder.HasIndex(payment => payment.LastReconciliationAttemptAt)
            .HasFilter("payment_method = 'PAYOS' AND status = 'PENDING' AND deleted_at IS NULL");
        builder.HasIndex(payment => new { payment.Status, payment.InitiatedAt }).IsDescending(false, true);
        builder.HasIndex(payment => new { payment.PayerFarmerProfileId, payment.InitiatedAt }).IsDescending(false, true);

        builder.HasCheck("amount", "amount > 0");
        builder.HasCheck(
            "order_context",
            "(payment_context = 'ORDER_PAYMENT' AND order_id IS NOT NULL) OR (payment_context = 'DEBT_REPAYMENT' AND order_id IS NULL)");
    }
}
