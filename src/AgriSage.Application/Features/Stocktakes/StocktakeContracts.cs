using AgriSage.Application.Common.Models;

namespace AgriSage.Application.Features.Stocktakes;

public sealed record CreateStocktakeRequest(IReadOnlyList<Guid>? StoreProductIds = null, bool IncludeEmptyLots = false, string? Note = null);
public sealed record StocktakeCountsRequest(IReadOnlyList<StocktakeCountRequest> Counts);
public sealed record StocktakeCountRequest(Guid ItemId, long CountedQuantity, decimal? UnitCost = null, string? ReasonCode = null, string? Note = null);
public sealed record CancelStocktakeRequest(string? Reason = null);
public sealed record StocktakeDetailRequest(bool OnlyDifferences = false, bool OnlyUncounted = false);
public sealed record StocktakeListRequest : PaginationRequest
{
    public string? Status { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public string? Search { get; init; }
}

public sealed record StocktakeListItem(Guid Id, string StocktakeNumber, string Status, int LineCount, int CountedCount,
    int DifferenceCount, Guid CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);
public sealed record StocktakeTotals(int Lines, int Counted, int WithDifference, decimal DifferenceCostValue);
public sealed record StocktakeMovement(Guid Id, string MovementNumber, string MovementType);
public sealed record StocktakeItemResponse(Guid Id, Guid InventoryLotId, string Sku, string ProductName, string? LotNumber,
    DateOnly? ExpiryDate, long SystemQuantitySnapshot, DateTimeOffset SnapshotAt, long? CountedQuantity,
    long? DifferenceQuantity, decimal? UnitCostSnapshot, decimal? DifferenceCostValue, string? ReasonCode,
    string? Note, Guid? CountedBy, DateTimeOffset? CountedAt, bool IsStale);
public sealed record StocktakeResponse(Guid Id, string StocktakeNumber, string Status, string? Note, Guid CreatedBy,
    DateTimeOffset CreatedAt, Guid? StartedBy, DateTimeOffset? StartedAt, Guid? CompletedBy, DateTimeOffset? CompletedAt,
    StocktakeTotals Totals, IReadOnlyList<StocktakeMovement> Movements, IReadOnlyList<StocktakeItemResponse> Items);

public interface IStocktakeService
{
    Task<StocktakeResponse> CreateAsync(CreateStocktakeRequest request, CancellationToken token);
    Task<PagedResult<StocktakeListItem>> ListAsync(StocktakeListRequest request, CancellationToken token);
    Task<StocktakeResponse> GetAsync(Guid id, StocktakeDetailRequest request, CancellationToken token);
    Task<StocktakeResponse> StartAsync(Guid id, CancellationToken token);
    Task<StocktakeResponse> CountAsync(Guid id, StocktakeCountsRequest request, CancellationToken token);
    Task<StocktakeResponse> RefreshStaleAsync(Guid id, CancellationToken token);
    Task<StocktakeResponse> CompleteAsync(Guid id, CancellationToken token);
    Task<StocktakeResponse> CancelAsync(Guid id, CancelStocktakeRequest request, CancellationToken token);
    Task DeleteAsync(Guid id, CancellationToken token);
}
