using AgriSage.Domain.Features.Deliveries.Entities;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Deliveries;

// Table 41: delivery_attempts.
internal sealed class DeliveryAttemptConfiguration : EntityConfiguration<DeliveryAttempt>
{
    protected override string TableName => "delivery_attempts";

    protected override void ConfigureEntity(EntityTypeBuilder<DeliveryAttempt> builder)
    {
        builder.Property(attempt => attempt.Status).HasMaxLength(30);
        builder.Property(attempt => attempt.FailureReasonCode).HasMaxLength(50);
        builder.Property(attempt => attempt.Note).HasMaxLength(1000);
        builder.Property(attempt => attempt.ReceiverName).HasMaxLength(150);
        builder.Property(attempt => attempt.ProofImageUrl).HasMaxLength(1000);

        builder.Ignore(attempt => attempt.DeliveredBaseQuantity);

        builder.HasReference<DeliveryAttempt, StoreMember>(attempt => attempt.AttemptedByMemberId);
        builder.HasReference<DeliveryAttempt, StockMovement>(attempt => attempt.SaleStockMovementId);
        builder.HasMany(attempt => attempt.Items).WithOne().HasForeignKey(item => item.DeliveryAttemptId);

        builder.HasIndex(attempt => new { attempt.DeliveryId, attempt.AttemptNumber }).IsUnique();
    }
}
