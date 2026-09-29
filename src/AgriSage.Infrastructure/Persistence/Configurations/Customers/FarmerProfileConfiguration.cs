using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Customers;

// Table 3: farmer_profiles. Gender stays a string (values not defined by the database design).
internal sealed class FarmerProfileConfiguration : EntityConfiguration<FarmerProfile>
{
    protected override string TableName => "farmer_profiles";

    protected override void ConfigureEntity(EntityTypeBuilder<FarmerProfile> builder)
    {
        builder.Property(profile => profile.Gender).HasMaxLength(20);
        builder.Property(profile => profile.Notes).HasMaxLength(1000);

        builder.HasOne(profile => profile.User).WithMany().HasForeignKey(profile => profile.UserId);

        builder.HasIndex(profile => profile.UserId).IsUnique();
    }
}
