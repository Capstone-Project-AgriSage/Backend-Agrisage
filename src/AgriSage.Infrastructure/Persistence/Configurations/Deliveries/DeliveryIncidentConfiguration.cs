using AgriSage.Domain.Features.Deliveries.Entities;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Deliveries;

// Table 43: delivery_incidents.
internal sealed class DeliveryIncidentConfiguration : EntityConfiguration<DeliveryIncident>
{
    protected override string TableName => "delivery_incidents";

    protected override void ConfigureEntity(EntityTypeBuilder<DeliveryIncident> builder)
    {
        builder.Property(incident => incident.IncidentType).HasMaxLength(40);
        builder.Property(incident => incident.Description).HasMaxLength(1000);
        builder.Property(incident => incident.Status).HasMaxLength(20);
        builder.Property(incident => incident.ResolutionType).HasMaxLength(40);
        builder.Property(incident => incident.ResolutionNote).HasMaxLength(1000);
        builder.Property(incident => incident.EvidenceImageUrl).HasMaxLength(1000);

        builder.HasReference<DeliveryIncident, Delivery>(incident => incident.DeliveryId);
        builder.HasReference<DeliveryIncident, DeliveryAttempt>(incident => incident.DeliveryAttemptId);
        builder.HasReference<DeliveryIncident, DeliveryItemLotAllocation>(incident => incident.DeliveryItemLotAllocationId);
        builder.HasReference<DeliveryIncident, StockMovement>(incident => incident.RelatedStockMovementId);
        builder.HasUserReference(incident => incident.ReportedBy);
        builder.HasUserReference(incident => incident.ResolvedBy);

        builder.HasIndex(incident => new { incident.DeliveryId, incident.Status });
    }
}
