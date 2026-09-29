namespace AgriSage.Application.Common.Models;

// Base for list requests; feature list requests inherit it and add their own search/status/sort filters.
public record PaginationRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    public int Skip => (Page - 1) * PageSize;
}
