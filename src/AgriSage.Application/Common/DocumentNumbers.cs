using AgriSage.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Common;

// Document numbers PREFIX-yyyyMMdd-NNNN, numbered per store (debt entries: globally) and per Vietnam day. The number is
// the highest one of the day plus one; a clash between two simultaneous creations is caught by the unique index and
// reported as a conflict (retry). Prefixes are fixed by the API contracts.
public static class DocumentNumbers
{
    public const string GoodsReceipt = "GR";
    public const string StockMovement = "SM";
    public const string Order = "OD";
    public const string Delivery = "DL";
    public const string Payment = "PM";
    public const string Stocktake = "ST";
    public const string SalesReturn = "RT";
    public const string Refund = "RF";
    public const string DebtEntry = "DE";

    public static string Format(string prefix, DateOnly day, int sequence) => $"{prefix}-{day:yyyyMMdd}-{sequence:D4}";

    // The sequence part of a number of the same prefix and day, or 0 when it does not follow the format.
    public static int SequenceOf(string number, string prefix, DateOnly day)
    {
        var start = $"{prefix}-{day:yyyyMMdd}-";

        return number.StartsWith(start, StringComparison.Ordinal) && int.TryParse(number[start.Length..], out var sequence)
            ? sequence
            : 0;
    }

    // existingNumbers: the number column of the documents in scope, including soft-deleted rows (the unique indexes
    // count them), e.g. context.Orders.IgnoreQueryFilters().Where(o => o.StoreId == storeId).Select(o => o.OrderNumber).
    public static async Task<string> NextAsync(
        IQueryable<string> existingNumbers, string prefix, DateOnly day, CancellationToken cancellationToken)
    {
        var start = $"{prefix}-{day:yyyyMMdd}-";
        var last = await existingNumbers
            .Where(number => number.StartsWith(start))
            .OrderByDescending(number => number)
            .FirstOrDefaultAsync(cancellationToken);

        return Format(prefix, day, (last is null ? 0 : SequenceOf(last, prefix, day)) + 1);
    }

    public static Task<string> NextReceiptNumberAsync(
        IAgriSageDbContext context, Guid storeId, DateOnly day, CancellationToken cancellationToken) =>
        NextAsync(
            context.GoodsReceipts.IgnoreQueryFilters().AsNoTracking().Where(r => r.StoreId == storeId).Select(r => r.ReceiptNumber),
            GoodsReceipt,
            day,
            cancellationToken);

    public static Task<string> NextMovementNumberAsync(
        IAgriSageDbContext context, Guid storeId, DateOnly day, CancellationToken cancellationToken) =>
        NextAsync(
            context.StockMovements.IgnoreQueryFilters().AsNoTracking().Where(m => m.StoreId == storeId).Select(m => m.MovementNumber),
            StockMovement,
            day,
            cancellationToken);
}
