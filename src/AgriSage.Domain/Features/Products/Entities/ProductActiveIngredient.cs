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
}
