using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Payments;

// Table 37: payment_allocations. Exactly one target (ORDER or DEBT).
internal sealed class PaymentAllocationConfiguration : EntityConfiguration<PaymentAllocation>
{
    protected override string TableName => "payment_allocations";

    protected override void ConfigureEntity(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.Property(allocation => allocation.AllocationType).HasMaxLength(20);
        builder.Property(allocation => allocation.AllocatedAmount).IsMoney();
        builder.Property(allocation => allocation.PrepaymentConsumedAmount).IsMoney().HasDbDefault(0m);
        builder.Property(allocation => allocation.Status).HasMaxLength(20);
        builder.Property(allocation => allocation.ReversalReason).HasMaxLength(500);

        builder.Ignore(allocation => allocation.IsActive);
        builder.Ignore(allocation => allocation.AvailablePrepayment);

        builder.HasReference<PaymentAllocation, Order>(allocation => allocation.OrderId);
        builder.HasReference<PaymentAllocation, DebtEntry>(allocation => allocation.DebtEntryId);
        builder.HasUserReference(allocation => allocation.AllocatedBy);
        builder.HasUserReference(allocation => allocation.ReversedBy);

        builder.HasIndex(allocation => allocation.Status);

        builder.HasCheck("allocated_amount", "allocated_amount > 0");
        builder.HasCheck("prepayment_consumed_amount", "prepayment_consumed_amount >= 0");
        builder.HasCheck("consumed_within_allocated", "prepayment_consumed_amount <= allocated_amount");
        builder.HasCheck(
            "single_target",
            "(allocation_type = 'ORDER' AND order_id IS NOT NULL AND debt_entry_id IS NULL) "
            + "OR (allocation_type = 'DEBT' AND debt_entry_id IS NOT NULL AND order_id IS NULL "
            + "AND prepayment_consumed_amount = 0)");
    }
}
