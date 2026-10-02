using AgriSage.Application.Common.Models;

namespace AgriSage.Application.Features.Products.Dtos.Requests;

public sealed record CreateCategoryRequest(string Code, string Name, string? Description, int DisplayOrder, Guid? ParentId);

// The code is fixed once created; only the other fields change.
public sealed record UpdateCategoryRequest(string Name, string? Description, int DisplayOrder, Guid? ParentId);

public sealed record CategoryListRequest : PaginationRequest
{
    public Guid? ParentId { get; init; }

    public bool? IsActive { get; init; }

    public string? Search { get; init; }
}

public sealed record BrandRequest(string Name, string? Code, string? Description, string? LogoUrl);

public sealed record BrandListRequest : PaginationRequest
{
    public bool? IsActive { get; init; }

    public string? Search { get; init; }
}

public sealed record ActiveIngredientRequest(string Name, string? Code, string? Description);

public sealed record ActiveIngredientListRequest : PaginationRequest
{
    public bool? IsActive { get; init; }

    public string? Search { get; init; }
}
