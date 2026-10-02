namespace AgriSage.Application.Features.Products.Dtos.Responses;

public sealed record CategoryResponse(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Name,
    string? Description,
    int DisplayOrder,
    bool IsActive);

public sealed record CategoryTreeNode(
    Guid Id,
    string Code,
    string Name,
    int DisplayOrder,
    bool IsActive,
    IReadOnlyList<CategoryTreeNode> Children);

public sealed record BrandResponse(Guid Id, string? Code, string Name, string? Description, string? LogoUrl, bool IsActive);

public sealed record ActiveIngredientResponse(Guid Id, string? Code, string Name, string? Description, bool IsActive);

public sealed record UnitResponse(Guid Id, string Code, string Name, string? Symbol, bool IsActive);
