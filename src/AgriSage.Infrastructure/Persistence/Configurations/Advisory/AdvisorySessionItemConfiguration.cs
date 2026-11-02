using AgriSage.Domain.Features.Advisory.Entities;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Advisory;

// Table advisory_session_items (DATABASE_DESIGN.md section XXIV-A): a product offered in a session.
internal sealed class AdvisorySessionItemConfiguration : EntityConfiguration<AdvisorySessionItem>
{
    protected override string TableName => "advisory_session_items";

    protected override void ConfigureEntity(EntityTypeBuilder<AdvisorySessionItem> builder)
    {
        builder.Property(item => item.EstimatedAmount).HasPrecision(14, 2);

        builder.HasReference<AdvisorySessionItem, AdvisorySession>(item => item.AdvisorySessionId);
        builder.HasReference<AdvisorySessionItem, StoreProduct>(item => item.StoreProductId);
        builder.HasReference<AdvisorySessionItem, DiseaseProduct>(item => item.DiseaseProductId);

        builder.HasCheck("suggested_base_quantity", "suggested_base_quantity IS NULL OR suggested_base_quantity > 0");
    }
}
