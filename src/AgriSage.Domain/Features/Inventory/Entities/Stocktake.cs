using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Inventory.Enums;

namespace AgriSage.Domain.Features.Inventory.Entities;

// Inventory count session: DRAFT (snapshot lines) → IN_PROGRESS (count) → COMPLETED.
// Adjustment Stock Movements for the differences are created by the completion use case.
public sealed class Stocktake : SoftDeletableEntity
{
    private readonly List<StocktakeItem> _items = [];

    private Stocktake()
    {
    }

    public Stocktake(Guid storeId, string stocktakeNumber, Guid createdBy, string? note = null)
    {
        StoreId = storeId;
        StocktakeNumber = Guard.NotNullOrWhiteSpace(stocktakeNumber);
        CreatedBy = createdBy;
        Note = note;
        Status = StocktakeStatus.Draft;
    }

    public Guid StoreId { get; private set; }

    public string StocktakeNumber { get; private set; } = null!;

    public StocktakeStatus Status { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public Guid? StartedBy { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public Guid? CompletedBy { get; private set; }

    public string? Note { get; private set; }

    public Guid CreatedBy { get; private set; }

    public IReadOnlyCollection<StocktakeItem> Items => _items.AsReadOnly();

    // snapshotAt: when systemQuantitySnapshot was read (after locking the Lot balance), database design §35.19.
    public StocktakeItem AddItem(
        Guid inventoryLotId,
        long systemQuantitySnapshot,
        DateTimeOffset snapshotAt,
        decimal? unitCostSnapshot = null)
    {
        EnsureStatus(StocktakeStatus.Draft);

        if (ActiveItems.Any(i => i.InventoryLotId == inventoryLotId))
        {
            throw new DomainException($"Stocktake '{StocktakeNumber}' already counts this inventory lot.");
        }

        var item = new StocktakeItem(Id, inventoryLotId, systemQuantitySnapshot, snapshotAt, unitCostSnapshot);
        _items.Add(item);

        return item;
    }

    public void Start(Guid startedBy, DateTimeOffset startedAt)
    {
        EnsureStatus(StocktakeStatus.Draft);

        if (!ActiveItems.Any())
        {
            throw new DomainException($"Stocktake '{StocktakeNumber}' has no lines to count.");
        }

        Status = StocktakeStatus.InProgress;
        StartedBy = startedBy;
        StartedAt = startedAt;
    }

    public void RecordCount(
        Guid itemId,
        long countedQuantity,
        Guid countedBy,
        DateTimeOffset countedAt,
        string? reasonCode = null,
        string? note = null)
    {
        EnsureStatus(StocktakeStatus.InProgress);

        var item = ActiveItems.SingleOrDefault(i => i.Id == itemId)
            ?? throw new DomainException($"Line '{itemId}' was not found on stocktake '{StocktakeNumber}'.");

        item.RecordCount(countedQuantity, countedBy, countedAt, reasonCode, note);
    }

    // A stale line (a movement for its Lot was posted between snapshot and count, decided by the use case) is
    // snapshotted again and its count cleared, so staff count only that Lot again (database design §35.19).
    public void RefreshItem(Guid itemId, long systemQuantitySnapshot, DateTimeOffset snapshotAt, decimal? unitCostSnapshot = null)
    {
        EnsureStatus(StocktakeStatus.InProgress);

        var item = ActiveItems.SingleOrDefault(i => i.Id == itemId)
            ?? throw new DomainException($"Line '{itemId}' was not found on stocktake '{StocktakeNumber}'.");

        item.Refresh(systemQuantitySnapshot, unitCostSnapshot, snapshotAt);
    }

    // Every line must be counted; an uncounted line blocks completion (database design §35.8).
    public void Complete(Guid completedBy, DateTimeOffset completedAt)
    {
        EnsureStatus(StocktakeStatus.InProgress);

        if (ActiveItems.Any(i => !i.IsCounted))
        {
            throw new DomainException($"Stocktake '{StocktakeNumber}' has uncounted lines.");
        }

        Status = StocktakeStatus.Completed;
        CompletedBy = completedBy;
        CompletedAt = completedAt;
    }

    public void Cancel()
    {
        if (Status is not (StocktakeStatus.Draft or StocktakeStatus.InProgress))
        {
            throw new DomainException($"Stocktake '{StocktakeNumber}' is {Status} and cannot be cancelled.");
        }

        Status = StocktakeStatus.Cancelled;
    }

    protected override void EnsureCanBeDeleted() => EnsureStatus(StocktakeStatus.Draft);

    private IEnumerable<StocktakeItem> ActiveItems => _items.Where(i => !i.IsDeleted);

    private void EnsureStatus(StocktakeStatus expected)
    {
        if (Status != expected)
        {
            throw new DomainException($"Stocktake '{StocktakeNumber}' is {Status}; expected {expected}.");
        }
    }
}
