using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Advisory.Entities;

// A product offered in an advisory session, with the quantity and amount computed for the Farmer's area.
public sealed class AdvisorySessionItem : SoftDeletableEntity
{
    private AdvisorySessionItem()
    {
    }

    public AdvisorySessionItem(
        Guid advisorySessionId,
        Guid storeProductId,
        Guid diseaseProductId,
        int rankOrder,
        long? suggestedBaseQuantity,
        decimal? estimatedAmount)
    {
        AdvisorySessionId = Guard.NotEmpty(advisorySessionId);
        StoreProductId = Guard.NotEmpty(storeProductId);
        DiseaseProductId = Guard.NotEmpty(diseaseProductId);
        RankOrder = rankOrder;
        SuggestedBaseQuantity = suggestedBaseQuantity is { } quantity ? Guard.Positive(quantity) : null;
        EstimatedAmount = estimatedAmount is { } amount ? Guard.NonNegativeMoney(amount) : null;
    }

    public Guid AdvisorySessionId { get; private set; }

    public Guid StoreProductId { get; private set; }

    public Guid DiseaseProductId { get; private set; }

    public int RankOrder { get; private set; }

    public long? SuggestedBaseQuantity { get; private set; }

    public decimal? EstimatedAmount { get; private set; }
}
