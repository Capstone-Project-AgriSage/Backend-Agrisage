using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;

namespace AgriSage.Application.Features.Inventory;

// A composable step: the endpoint owns the transaction, locks, SaveChanges and commit.
public sealed class StockAdjustmentPosting(IAgriSageDbContext context, IDateTimeProvider clock)
{
    public sealed record Line(InventoryLot Lot, long Delta, decimal? UnitCost, string? Note = null);

    public async Task<IReadOnlyList<StockMovement>> PostAsync(Guid storeId, Guid actorId, IReadOnlyList<Line> lines,
        string reasonCode, string? reason, Guid? stocktakeId, CancellationToken token)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var line in lines)
        {
            var balance = line.Lot.Balance;
            if (line.Delta == long.MinValue || (line.Delta < 0 && -line.Delta > balance.AvailableQuantity))
            {
                errors[line.Lot.Id.ToString()] = ["The decrease would consume reserved stock or make stock negative."];
            }
            else if (line.Delta > 0 && (line.UnitCost ?? balance.AverageUnitCost) is null)
            {
                errors[line.Lot.Id.ToString()] = ["A unit cost is required when adding stock to an empty lot."];
            }
            else if (line.Delta > 0 && line.Delta > long.MaxValue - balance.QuantityOnHand)
            {
                errors[line.Lot.Id.ToString()] = ["The resulting quantity is too large."];
            }
        }

        if (errors.Count > 0)
        {
            throw new BusinessRuleException("Some lots cannot be adjusted.", errors);
        }

        var groups = lines.Where(l => l.Delta != 0).GroupBy(l => l.Delta > 0).OrderByDescending(g => g.Key).ToList();
        if (groups.Count == 0)
        {
            return [];
        }

        var now = clock.UtcNow;
        var day = BusinessCalendar.Today(now);
        var first = await DocumentNumbers.NextMovementNumberAsync(context, storeId, day, token);
        var sequence = DocumentNumbers.SequenceOf(first, DocumentNumbers.StockMovement, day);
        var movements = new List<StockMovement>();
        foreach (var group in groups)
        {
            var movement = new StockMovement(storeId, DocumentNumbers.Format(DocumentNumbers.StockMovement, day, sequence++),
                group.Key ? StockMovementType.AdjustmentIn : StockMovementType.AdjustmentOut, now, actorId,
                stocktakeId: stocktakeId, reasonCode: reasonCode, reason: reason);
            foreach (var line in group.OrderBy(l => l.Lot.Id))
            {
                var change = line.Delta > 0
                    ? line.Lot.ReceiveStock(line.Delta, line.UnitCost ?? line.Lot.Balance.AverageUnitCost!.Value)
                    : line.Lot.IssueUnreserved(-line.Delta);
                movement.AddItem(line.Lot.Id, change, line.Note);
            }

            movement.Post(actorId, now);
            context.StockMovements.Add(movement);
            movements.Add(movement);
        }

        return movements;
    }
}

internal static class InventoryActors
{
    public static Guid Require(ICurrentUserService user, bool manage = false)
    {
        var id = user.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        if (user.Role is not ("ADMIN" or "STORE_OWNER") && (manage || user.Role != "SALES_STAFF"))
        {
            throw new ForbiddenException();
        }

        return id;
    }
}
