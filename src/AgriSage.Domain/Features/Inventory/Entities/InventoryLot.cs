using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Products.Entities;

namespace AgriSage.Domain.Features.Inventory.Entities;

// Aggregate root for one logical Lot and its Balance. Same Store Product + normalized lot number +
// expiry date = same logical Lot; resolving that identity is done by Application + unique index.
// Every physical change here must be recorded in a posted Stock Movement by the calling use case.
public sealed class InventoryLot : SoftDeletableEntity
{
    private InventoryLot()
    {
    }

    public InventoryLot(
        Guid storeProductId,
        string? lotNumber = null,
        DateOnly? manufacturingDate = null,
        DateOnly? expiryDate = null)
    {
        StoreProductId = storeProductId;
        LotNumber = lotNumber;
        ManufacturingDate = manufacturingDate;
        ExpiryDate = expiryDate;
        Status = InventoryLotStatus.Active;
        Balance = new InventoryLotBalance(Id);
    }

    public Guid StoreProductId { get; private set; }

    public StoreProduct StoreProduct { get; private set; } = null!;

    public string? LotNumber { get; private set; }

    public DateOnly? ManufacturingDate { get; private set; }

    public DateOnly? ExpiryDate { get; private set; }

    public InventoryLotStatus Status { get; private set; }

    public InventoryLotBalance Balance { get; private set; } = null!;

    // No automatic Expired/Depleted transitions; status changes are explicit.
    public void ChangeStatus(InventoryLotStatus status) => Status = status;

    // Align the status with the business date without changing stock or overriding a manual hold.
    public bool ExpireIfDue(DateOnly today)
    {
        if (Status != InventoryLotStatus.Active || ExpiryDate is null || ExpiryDate >= today)
        {
            return false;
        }

        Status = InventoryLotStatus.Expired;
        return true;
    }

    // Expired (expiry_date < today), quarantined, blocked or depleted Lots cannot be reserved, allocated or sold.
    public bool IsEligibleForSale(DateOnly today) =>
        Status == InventoryLotStatus.Active && (ExpiryDate is null || ExpiryDate >= today);

    // Reservation changes quantity_reserved only; no Stock Movement.
    public void Reserve(long quantity, DateOnly today)
    {
        EnsureEligibleForSale(today);
        Balance.Reserve(quantity);
    }

    public void ReleaseReservation(long quantity) => Balance.ReleaseReservation(quantity);

    // STOCK_IN, RETURN_IN, ADJUSTMENT_IN and reversal of an outflow.
    public LotBalanceChange ReceiveStock(long quantity, decimal unitCost) => Balance.Increase(quantity, unitCost);

    // SALE: issues previously reserved quantity at the current Weighted Average Cost.
    public LotBalanceChange IssueReserved(long quantity, DateOnly today)
    {
        EnsureEligibleForSale(today);
        return Balance.DecreaseAtAverageCost(quantity, fromReserved: true);
    }

    // ADJUSTMENT_OUT: issues unreserved quantity at the current Weighted Average Cost.
    public LotBalanceChange IssueUnreserved(long quantity) =>
        Balance.DecreaseAtAverageCost(quantity, fromReserved: false);

    // Reversal of an inflow (e.g. STOCK_IN) at that inflow's unit cost.
    public LotBalanceChange RemoveStockAtCost(long quantity, decimal unitCost) =>
        Balance.DecreaseAtCost(quantity, unitCost);

    private void EnsureEligibleForSale(DateOnly today)
    {
        if (!IsEligibleForSale(today))
        {
            throw new DomainException(
                $"Inventory lot '{LotNumber ?? Id.ToString()}' is not eligible for sale (status {Status}, expiry {ExpiryDate}).");
        }
    }
}
