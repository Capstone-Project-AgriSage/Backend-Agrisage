using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Stores;

// Table 5: stores. "Only one Store may be ACTIVE" is an application rule.
internal sealed class StoreConfiguration : EntityConfiguration<Store>
{
    protected override string TableName => "stores";

    protected override void ConfigureEntity(EntityTypeBuilder<Store> builder)
    {
        builder.Property(store => store.Code).HasMaxLength(30);
        builder.Property(store => store.Name).HasMaxLength(200);
        builder.Property(store => store.PhoneNumber).HasMaxLength(20);
        builder.Property(store => store.Email).HasMaxLength(255);
        builder.Property(store => store.TaxCode).HasMaxLength(50);
        builder.Property(store => store.AddressLine).HasMaxLength(500);
        builder.Property(store => store.Ward).HasMaxLength(150);
        builder.Property(store => store.District).HasMaxLength(150);
        builder.Property(store => store.Province).HasMaxLength(150);
        builder.Property(store => store.Status).HasMaxLength(20);

        builder.HasIndex(store => store.Code).IsUnique();
    }
}
