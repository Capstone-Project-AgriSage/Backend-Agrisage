using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Pricing.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Pricing;

// Table 20: customer_group_price_lists. "One applicable Price List at a time" is a temporal
// Application rule, not a simple unique index.
internal sealed class CustomerGroupPriceListConfiguration : EntityConfiguration<CustomerGroupPriceList>
{
    protected override string TableName => "customer_group_price_lists";

    protected override void ConfigureEntity(EntityTypeBuilder<CustomerGroupPriceList> builder)
    {
        builder.HasReference<CustomerGroupPriceList, CustomerGroup>(mapping => mapping.CustomerGroupId);
        builder.HasOne(mapping => mapping.PriceList).WithMany().HasForeignKey(mapping => mapping.PriceListId);
        builder.HasUserReference(mapping => mapping.AssignedBy);

        builder.HasIndex(mapping => new { mapping.CustomerGroupId, mapping.EffectiveFrom }).IsDescending(false, true);

        builder.HasCheck("effective_period", "effective_to IS NULL OR effective_to > effective_from");
    }
}
