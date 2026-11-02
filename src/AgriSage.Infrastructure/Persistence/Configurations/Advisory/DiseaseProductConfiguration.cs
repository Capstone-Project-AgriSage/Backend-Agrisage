using AgriSage.Domain.Features.Advisory.Entities;
using AgriSage.Domain.Features.Advisory.Enums;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Advisory;

// Table disease_products (DATABASE_DESIGN.md section XXIV-A): the reviewed link between a problem and a product.
internal sealed class DiseaseProductConfiguration : EntityConfiguration<DiseaseProduct>
{
    protected override string TableName => "disease_products";

    protected override void ConfigureEntity(EntityTypeBuilder<DiseaseProduct> builder)
    {
        builder.Property(link => link.RecommendationGroup).HasMaxLength(20);
        builder.Property(link => link.RegistrationSource).HasMaxLength(200);
        builder.Property(link => link.Reason).HasMaxLength(300);
        builder.Property(link => link.DoseText).HasMaxLength(500);
        builder.Property(link => link.DoseSource).HasMaxLength(200);
        builder.Property(link => link.DosePerHaBase).HasPrecision(12, 3);
        builder.Property(link => link.Applications).HasDbDefault(1);
        builder.Property(link => link.GrowthStages).HasMaxLength(100);
        builder.Property(link => link.Priority).HasDbDefault(0);
        builder.Property(link => link.Status).HasMaxLength(20).HasDbDefault(DiseaseProductStatus.Draft);
        builder.Ignore(link => link.StageList);

        builder.HasOne(link => link.Disease).WithMany().HasForeignKey(link => link.DiseaseId);
        builder.HasOne(link => link.Product).WithMany().HasForeignKey(link => link.ProductId);
        builder.HasUserReference(link => link.ApprovedBy);

        builder.HasIndex(link => new { link.DiseaseId, link.ProductId }).IsUnique().HasFilter("deleted_at IS NULL");
        builder.HasIndex(link => new { link.DiseaseId, link.Status });

        builder.HasCheck("registration_source", "recommendation_group <> 'PROTECTION' OR registration_source IS NOT NULL");
        builder.HasCheck("reason", "recommendation_group <> 'NUTRITION' OR reason IS NOT NULL");
        builder.HasCheck("dose_per_ha_base", "dose_per_ha_base IS NULL OR dose_per_ha_base > 0");
        builder.HasCheck("applications", "applications BETWEEN 1 AND 10");
        builder.HasCheck("pre_harvest_days", "pre_harvest_days IS NULL OR pre_harvest_days >= 0");
        builder.HasCheck("approval", "status <> 'APPROVED' OR (approved_by IS NOT NULL AND approved_at IS NOT NULL)");
    }
}
