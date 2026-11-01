using AgriSage.Domain.Features.Advisory.Entities;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Advisory;

// Table advisory_sessions (DATABASE_DESIGN.md section XXIV-A): one recommendation given to a Farmer.
internal sealed class AdvisorySessionConfiguration : EntityConfiguration<AdvisorySession>
{
    protected override string TableName => "advisory_sessions";

    protected override void ConfigureEntity(EntityTypeBuilder<AdvisorySession> builder)
    {
        builder.Property(session => session.Source).HasMaxLength(20);
        builder.Property(session => session.QueryText).HasMaxLength(200);
        builder.Property(session => session.AreaSqm).HasPrecision(12, 1);
        builder.Property(session => session.GrowthStage).HasMaxLength(20);

        builder.HasReference<AdvisorySession, Store>(session => session.StoreId);
        builder.HasReference<AdvisorySession, FarmerProfile>(session => session.FarmerProfileId);
        builder.HasReference<AdvisorySession, Disease>(session => session.DiseaseId);
        builder.HasReference<AdvisorySession, DiagnosisCase>(session => session.DiagnosisCaseId);

        builder.HasIndex(session => new { session.StoreId, session.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(session => new { session.FarmerProfileId, session.DiseaseId, session.CreatedAt })
            .IsDescending(false, false, true);

        builder.HasCheck("area_sqm", "area_sqm IS NULL OR area_sqm > 0");
    }
}
