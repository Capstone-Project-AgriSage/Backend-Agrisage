using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Customers;

// Table 8: customer_group_assignments. One current group per Farmer (effective_to IS NULL).
internal sealed class CustomerGroupAssignmentConfiguration : EntityConfiguration<CustomerGroupAssignment>
{
    protected override string TableName => "customer_group_assignments";

    protected override void ConfigureEntity(EntityTypeBuilder<CustomerGroupAssignment> builder)
    {
        builder.Property(assignment => assignment.Reason).HasMaxLength(500);

        builder.HasReference<CustomerGroupAssignment, FarmerProfile>(assignment => assignment.FarmerProfileId);
        builder.HasOne(assignment => assignment.CustomerGroup).WithMany().HasForeignKey(assignment => assignment.CustomerGroupId);
        builder.HasUserReference(assignment => assignment.AssignedBy);

        builder.HasIndex(assignment => new { assignment.FarmerProfileId, assignment.EffectiveFrom }).IsDescending(false, true);
        builder.HasIndex(assignment => new { assignment.CustomerGroupId, assignment.EffectiveFrom }).IsDescending(false, true);
        builder.HasIndex(assignment => assignment.FarmerProfileId, "ux_customer_group_assignments_current")
            .IsUnique()
            .HasFilter("effective_to IS NULL AND deleted_at IS NULL")
            .HasDatabaseName("ux_customer_group_assignments_current");

        builder.HasCheck("effective_period", "effective_to IS NULL OR effective_to > effective_from");
    }
}
