using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Products.Entities;

public sealed class ActiveIngredient : SoftDeletableEntity
{
    private ActiveIngredient()
    {
    }

    public ActiveIngredient(string name, string? code = null, string? description = null)
    {
        Update(name, code, description);
        IsActive = true;
    }

    public string? Code { get; private set; }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, string? code, string? description)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Code = code;
        Description = description;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
