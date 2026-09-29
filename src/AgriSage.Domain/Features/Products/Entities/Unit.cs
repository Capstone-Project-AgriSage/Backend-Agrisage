using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Products.Entities;

// Packaging unit reference data (BOTTLE, BOX, CARTON, ...).
public sealed class Unit : SoftDeletableEntity
{
    private Unit()
    {
    }

    public Unit(string code, string name, string? symbol = null)
    {
        Code = Guard.NotNullOrWhiteSpace(code);
        Update(name, symbol);
        IsActive = true;
    }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Symbol { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, string? symbol)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Symbol = symbol;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
