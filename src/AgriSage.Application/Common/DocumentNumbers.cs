using AgriSage.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Common;

// Document numbers: GR-yyyyMMdd-NNNN (goods receipts) and SM-yyyyMMdd-NNNN (stock movements), numbered per store and
// per Vietnam day. The number is the highest one of the day plus one; a clash between two simultaneous creations is
// caught by the unique index and reported as a conflict (retry).
public static class DocumentNumbers
{
    public static string Format(string prefix, DateOnly day, int sequence) => $"{prefix}-{day:yyyyMMdd}-{sequence:D4}";

    // The sequence part of a number of the same prefix and day, or 0 when it does not follow the format.
    public static int SequenceOf(string number, string prefix, DateOnly day)
    {
        var start = $"{prefix}-{day:yyyyMMdd}-";

        return number.StartsWith(start, StringComparison.Ordinal) && int.TryParse(number[start.Length..], out var sequence)
            ? sequence
            : 0;
    }

    public static async Task<string> NextReceiptNumberAsync(
        IAgriSageDbContext context, Guid storeId, DateOnly day, CancellationToken cancellationToken)
    {
        var start = $"GR-{day:yyyyMMdd}-";
        var last = await context.GoodsReceipts.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.StoreId == storeId && r.ReceiptNumber.StartsWith(start))
            .OrderByDescending(r => r.ReceiptNumber).Select(r => r.ReceiptNumber).FirstOrDefaultAsync(cancellationToken);

        return Format("GR", day, (last is null ? 0 : SequenceOf(last, "GR", day)) + 1);
    }

    public static async Task<string> NextMovementNumberAsync(
        IAgriSageDbContext context, Guid storeId, DateOnly day, CancellationToken cancellationToken)
    {
        var start = $"SM-{day:yyyyMMdd}-";
        var last = await context.StockMovements.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.StoreId == storeId && m.MovementNumber.StartsWith(start))
            .OrderByDescending(m => m.MovementNumber).Select(m => m.MovementNumber).FirstOrDefaultAsync(cancellationToken);

        return Format("SM", day, (last is null ? 0 : SequenceOf(last, "SM", day)) + 1);
    }
}
