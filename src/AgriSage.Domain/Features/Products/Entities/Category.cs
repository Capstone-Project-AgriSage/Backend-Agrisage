using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;

namespace AgriSage.Domain.Features.Products.Entities;

public sealed class Category : SoftDeletableEntity
{
    private Category()
    {
    }

    public Category(string code, string name, Guid? parentId = null, string? description = null, int displayOrder = 0)
    {
        Code = Guard.NotNullOrWhiteSpace(code);
        Update(name, description, displayOrder);
        MoveTo(parentId);
        IsActive = true;
    }

    public Guid? ParentId { get; private set; }

    public Category? Parent { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public int DisplayOrder { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, string? description, int displayOrder)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Description = description;
        DisplayOrder = displayOrder;
    }

    // Deeper cycles (A → B → A) need the category tree and are checked by Application.
    public void MoveTo(Guid? parentId)
    {
        if (parentId == Id)
        {
            throw new DomainException("A category cannot be its own parent.");
        }

        ParentId = parentId;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
