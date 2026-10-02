using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Products.Entities;

public sealed class ProductActiveIngredient : SoftDeletableEntity
{
    private ProductActiveIngredient()
    {
    }

    public ProductActiveIngredient(
        Guid productId,
        Guid activeIngredientId,
        string? concentration = null,
        string? note = null)
    {
        ProductId = productId;
        ActiveIngredientId = activeIngredientId;
        Update(concentration, note);
    }

    public Guid ProductId { get; private set; }

    public Guid ActiveIngredientId { get; private set; }

    public ActiveIngredient ActiveIngredient { get; private set; } = null!;

    public string? Concentration { get; private set; }

    public string? Note { get; private set; }

    public void Update(string? concentration, string? note)
    {
        Concentration = concentration;
        Note = note;
    }

    // UNIQUE(product_id, active_ingredient_id) also counts deleted rows, so re-adding a removed ingredient
    // revives the old row (database design §35.16).
    public void Reinstate(string? concentration, string? note)
    {
        Restore();
        Update(concentration, note);
    }
}
