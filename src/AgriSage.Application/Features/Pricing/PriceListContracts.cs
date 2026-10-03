using AgriSage.Application.Common.Models;

namespace AgriSage.Application.Features.Pricing;

// FLOW_1 §3. A new list starts DRAFT; the code is immutable after creation.
public sealed record PriceListRequest(
    string Code,
    string Name,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo = null,
    bool IsWalkInDefault = false,
    string? Description = null);

public sealed record UpdatePriceListRequest(
    string Name,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo = null,
    bool IsWalkInDefault = false,
    string? Description = null);

// Status: DRAFT / ACTIVE / INACTIVE. Search: code or name.
public sealed record PriceListListRequest : PaginationRequest
{
    public string? Status { get; init; }

    public bool? IsWalkInDefault { get; init; }

    public string? Search { get; init; }
}

// Search: SKU or product name.
public sealed record PriceListItemListRequest : PaginationRequest
{
    public string? Search { get; init; }
}

public sealed record PriceListItemInput(Guid StoreProductId, Guid ProductPackagingId, decimal SellingPrice);

// Bulk upsert: one row per (store product, packaging); at most 500 lines per call.
public sealed record UpsertPriceListItemsRequest(IReadOnlyList<PriceListItemInput> Items);

public sealed record UpsertPriceListItemsResult(int Created, int Updated);

public sealed record PriceListGroupReference(Guid Id, string Code, string Name);

public sealed record PriceListResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsWalkInDefault,
    string Status,
    int ItemCount,
    IReadOnlyList<PriceListGroupReference> Groups,
    DateTimeOffset CreatedAt);

public sealed record PriceListItemResponse(
    Guid Id,
    Guid StoreProductId,
    Guid ProductPackagingId,
    string Sku,
    string ProductName,
    string PackagingName,
    decimal SellingPrice);

public interface IPriceListService
{
    Task<PriceListResponse> CreateAsync(PriceListRequest request, CancellationToken cancellationToken);

    Task<PriceListResponse> UpdateAsync(Guid id, UpdatePriceListRequest request, CancellationToken cancellationToken);

    Task<PriceListResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<PriceListResponse>> ListAsync(PriceListListRequest request, CancellationToken cancellationToken);

    // At most one ACTIVE walk-in default list per store (422 otherwise).
    Task<PriceListResponse> ActivateAsync(Guid id, CancellationToken cancellationToken);

    Task<PriceListResponse> DeactivateAsync(Guid id, CancellationToken cancellationToken);

    // Soft delete of a DRAFT list never linked to a group and never used by an order (409 otherwise: deactivate it).
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<PriceListItemResponse>> ListItemsAsync(
        Guid id, PriceListItemListRequest request, CancellationToken cancellationToken);

    // Only ACTIVE sale packagings of the store's products; affects carts and new orders only (orders keep snapshots).
    Task<UpsertPriceListItemsResult> UpsertItemsAsync(
        Guid id, UpsertPriceListItemsRequest request, CancellationToken cancellationToken);

    Task DeleteItemAsync(Guid id, Guid itemId, CancellationToken cancellationToken);
}
