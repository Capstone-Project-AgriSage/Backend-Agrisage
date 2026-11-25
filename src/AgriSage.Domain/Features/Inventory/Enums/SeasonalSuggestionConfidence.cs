namespace AgriSage.Domain.Features.Inventory.Enums;

// How strongly a product is suggested; a level, never a probability (F4.7). Stored as HIGH / MEDIUM / LOW.
public enum SeasonalSuggestionConfidence
{
    High,
    Medium,
    Low
}
