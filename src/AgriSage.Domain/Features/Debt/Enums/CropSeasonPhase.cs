namespace AgriSage.Domain.Features.Debt.Enums;

// Phase of a debt-collection crop season (decision D2): derived from the Vietnam date, never stored. Only CLOSED is a
// recorded fact (closed_at / closed_by), set by the store owner.
public enum CropSeasonPhase
{
    Planned,
    Open,
    Harvesting,
    Collecting,
    Closed
}
