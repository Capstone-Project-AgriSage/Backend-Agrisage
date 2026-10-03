using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Returns;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Payments;

// Payment reads (task F1.3, FLOW_1 §5), for staff and, with farmerProfileId, for one Farmer's own payments: another
// Farmer's payment or order is "not found". L2 and L3 reuse these queries (they show payOS payments and repayments).
public sealed class PaymentQueries(IAgriSageDbContext context)
{
    private sealed record Row(
        Guid Id,
        string PaymentNumber,
        PaymentContext PaymentContext,
        PaymentMethod PaymentMethod,
        decimal Amount,
        PaymentStatus Status,
        string? PayerName,
        DateTimeOffset? ConfirmedAt,
        DateTimeOffset InitiatedAt);

    // The farmer profile of the signed-in user, or null for a user without one.
    public Task<Guid?> FindFarmerProfileIdAsync(Guid userId, CancellationToken cancellationToken) =>
        context.FarmerProfiles.AsNoTracking().Where(f => f.UserId == userId)
            .Select(f => (Guid?)f.Id).FirstOrDefaultAsync(cancellationToken);

    public async Task<PaymentResponse> GetAsync(Guid id, Guid? farmerProfileId, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var payment = await context.Payments.AsNoTracking().Include(p => p.Allocations)
            .FirstOrDefaultAsync(p => p.Id == id && p.StoreId == storeId, cancellationToken);

        if (payment is null || (farmerProfileId is { } own && payment.PayerFarmerProfileId != own))
        {
            throw new NotFoundException("Payment", id);
        }

        var payerName = (await Rows(context.Payments.Where(p => p.Id == id)).SingleAsync(cancellationToken)).PayerName;
        var allocations = payment.Allocations.Where(a => !a.IsDeleted).OrderBy(a => a.AllocatedAt).ThenBy(a => a.Id).ToList();

        var orderIds = allocations.Where(a => a.OrderId is not null).Select(a => a.OrderId!.Value).Distinct().ToList();
        var orderNumbers = await context.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.OrderNumber, cancellationToken);
        var entryIds = allocations.Where(a => a.DebtEntryId is not null).Select(a => a.DebtEntryId!.Value).Distinct().ToList();
        var entryNumbers = await context.DebtEntries.AsNoTracking().Where(e => entryIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.EntryNumber, cancellationToken);

        return new PaymentResponse(
            payment.Id,
            payment.PaymentNumber,
            EnumText.Format(payment.PaymentContext),
            PaymentText.Format(payment.PaymentMethod),
            payment.Amount,
            payment.Currency,
            EnumText.Format(payment.Status),
            payment.PayerFarmerProfileId,
            payerName,
            payment.ConfirmationSource is { } source ? PaymentText.Format(source) : null,
            payment.ConfirmedBy,
            payment.ConfirmedAt,
            payment.CheckoutUrl,
            payment.ProviderOrderCode,
            payment.InitiatedAt,
            payment.FailedAt,
            payment.CancelledAt,
            payment.Note,
            payment.UnallocatedAmount,
            allocations.Select(a => new PaymentAllocationResponse(
                a.Id,
                EnumText.Format(a.AllocationType),
                a.OrderId,
                a.OrderId is { } orderId && orderNumbers.TryGetValue(orderId, out var number) ? number : null,
                a.DebtEntryId,
                a.DebtEntryId is { } entryId && entryNumbers.TryGetValue(entryId, out var entry) ? entry : null,
                a.AllocatedAmount,
                a.PrepaymentConsumedAmount,
                EnumText.Format(a.Status),
                a.AllocatedAt)).ToList());
    }

    public async Task<PagedResult<PaymentListItem>> ListAsync(
        PaymentListRequest request, Guid? farmerProfileId, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var query = context.Payments.AsNoTracking().Where(p => p.StoreId == storeId);

        if (farmerProfileId is { } own)
        {
            query = query.Where(p => p.PayerFarmerProfileId == own);
        }
        else if (request.FarmerProfileId is { } farmerId)
        {
            query = query.Where(p => p.PayerFarmerProfileId == farmerId);
        }

        if (EnumText.TryParse<PaymentContext>(request.PaymentContext, out var paymentContext))
        {
            query = query.Where(p => p.PaymentContext == paymentContext);
        }

        if (PaymentText.TryParseMethod(request.PaymentMethod, out var method))
        {
            query = query.Where(p => p.PaymentMethod == method);
        }

        if (EnumText.TryParse<PaymentStatus>(request.Status, out var status))
        {
            query = query.Where(p => p.Status == status);
        }

        if (request.OrderId is { } orderId)
        {
            query = query.Where(p => p.OrderId == orderId);
        }

        if (request.FromDate is { } from)
        {
            var start = BusinessCalendar.StartOfDay(from);
            query = query.Where(p => p.InitiatedAt >= start);
        }

        if (request.ToDate is { } to)
        {
            var end = BusinessCalendar.StartOfDay(to.AddDays(1));
            query = query.Where(p => p.InitiatedAt < end);
        }

        if (Texts.Clean(request.Search) is { } search)
        {
            var term = search.ToLower();
            query = query.Where(p => p.PaymentNumber.ToLower().Contains(term));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var rows = await Rows(query)
            .Skip(request.Skip).Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<PaymentListItem>(rows.Select(ToListItem).ToList(), request.Page, request.PageSize, total);
    }

    public async Task<OrderPaymentSummary> GetOrderSummaryAsync(Guid orderId, Guid? farmerProfileId, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var order = await context.Orders.AsNoTracking()
            .Where(o => o.Id == orderId && o.StoreId == storeId)
            .Select(o => new { o.TotalAmount, o.FarmerProfileId })
            .FirstOrDefaultAsync(cancellationToken);

        if (order is null || (farmerProfileId is { } own && order.FarmerProfileId != own))
        {
            throw new NotFoundException("Order", orderId);
        }

        var payments = (await Rows(context.Payments.Where(p => p.OrderId == orderId), newestFirst: false)
                .ToListAsync(cancellationToken))
            .Select(ToListItem).ToList();

        var allocations = await context.PaymentAllocations.AsNoTracking()
            .Where(a => a.OrderId == orderId && a.Status == PaymentAllocationStatus.Active)
            .Join(context.Payments.Where(p => p.Status == PaymentStatus.Paid), a => a.PaymentId, p => p.Id, (a, p) => a)
            .Select(a => new { a.AllocatedAmount, a.PrepaymentConsumedAmount })
            .ToListAsync(cancellationToken);
        var paid = allocations.Sum(a => a.AllocatedAmount);
        var consumed = allocations.Sum(a => a.PrepaymentConsumedAmount);

        var refunds = await context.Refunds.AsNoTracking().Where(r => r.OrderId == orderId)
            .OrderBy(r => r.RequestedAt).ThenBy(r => r.Id).ToListAsync(cancellationToken);

        return new OrderPaymentSummary(
            orderId, order.TotalAmount, paid, paid - consumed, consumed, Math.Max(0m, order.TotalAmount - paid), payments,
            refunds.Select(RefundResponse.From).ToList());
    }

    // Payer = the paying Farmer, else the order's customer (a walk-in). Ordered before the projection, which EF cannot
    // order by a constructed row.
    private IQueryable<Row> Rows(IQueryable<Payment> payments, bool newestFirst = true)
    {
        var joined =
            from p in payments
            join f in context.FarmerProfiles on p.PayerFarmerProfileId equals (Guid?)f.Id into farmers
            from farmer in farmers.DefaultIfEmpty()
            join o in context.Orders on p.OrderId equals (Guid?)o.Id into orders
            from order in orders.DefaultIfEmpty()
            select new { Payment = p, Farmer = farmer, Order = order };

        var ordered = newestFirst
            ? joined.OrderByDescending(x => x.Payment.InitiatedAt).ThenByDescending(x => x.Payment.PaymentNumber)
            : joined.OrderBy(x => x.Payment.InitiatedAt).ThenBy(x => x.Payment.PaymentNumber);

        return ordered.Select(x => new Row(
            x.Payment.Id, x.Payment.PaymentNumber, x.Payment.PaymentContext, x.Payment.PaymentMethod, x.Payment.Amount,
            x.Payment.Status,
            x.Farmer != null ? x.Farmer.User.FullName : x.Order != null ? x.Order.CustomerNameSnapshot : null,
            x.Payment.ConfirmedAt, x.Payment.InitiatedAt));
    }

    private static PaymentListItem ToListItem(Row row) => new(
        row.Id,
        row.PaymentNumber,
        EnumText.Format(row.PaymentContext),
        PaymentText.Format(row.PaymentMethod),
        row.Amount,
        EnumText.Format(row.Status),
        row.PayerName,
        row.ConfirmedAt,
        row.InitiatedAt);
}
