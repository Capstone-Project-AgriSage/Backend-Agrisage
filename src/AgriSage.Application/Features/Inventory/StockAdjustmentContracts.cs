namespace AgriSage.Application.Features.Inventory;

public sealed record StockAdjustmentRequest(string ReasonCode, string Note, IReadOnlyList<StockAdjustmentLine> Lines);
public sealed record StockAdjustmentLine(Guid InventoryLotId, long QuantityDeltaBase, decimal? UnitCost = null);

public interface IStockAdjustmentService
{
    Task<StockMovementResponse> CreateAsync(StockAdjustmentRequest request, CancellationToken token);
}
