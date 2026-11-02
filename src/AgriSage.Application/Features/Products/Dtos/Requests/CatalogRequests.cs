using AgriSage.Application.Common.Models;

namespace AgriSage.Application.Features.Products.Dtos.Requests;

// DisplayOrder is zero-based for the API. Null means append to the end of the sibling list.
public sealed record CreateCategoryRequest(string Code, string Name, string? Description, int? DisplayOrder, Guid? ParentId);

// The code is fixed once created; only the other fields change.
// DisplayOrder is zero-based for the API. Null preserves the current position when the parent is unchanged,
// or appends when the category is moved to a new parent.
public sealed record UpdateCategoryRequest(string Name, string? Description, int? DisplayOrder, Guid? ParentId);

public sealed record ReorderCategoryRequest(int Position);

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
