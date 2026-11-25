using System.Text.Json;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Invoices.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Invoices;

public sealed class InvoiceService(IAgriSageDbContext context, ICurrentUserService user, IDateTimeProvider clock,
    PaymentQueries payments, IInvoicePdfRenderer renderer, IInvoiceEmailSender sender, AuditTrail audit) : IInvoiceService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private Guid Actor => user.UserId ?? throw new ForbiddenException();

    public async Task<InvoiceIssueResult> IssueAsync(Guid orderId, CancellationToken token)
    {
        var storeId = await ActiveStore.GetIdAsync(context, token);
        var existing = await context.SalesInvoices.AsNoTracking().Where(x => x.OrderId == orderId && x.StoreId == storeId)
            .Select(x => new { x.Id }).SingleOrDefaultAsync(token);
        if (existing is not null) return new(await GetAsync(existing.Id, token), false);

        var order = await context.Orders.AsNoTracking().Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == orderId && x.StoreId == storeId, token) ?? throw new NotFoundException("Order", orderId);
        if (order.Status != OrderStatus.Completed)
            throw new BusinessRuleException("Chỉ có thể tạo hóa đơn cho đơn hàng đã hoàn thành.");
        var store = await context.Stores.AsNoTracking().SingleAsync(x => x.Id == storeId, token);
        var cashier = await context.Users.AsNoTracking().Where(x => x.Id == Actor).Select(x => x.FullName).SingleAsync(token);
        var email = order.FarmerProfileId is { } farmerId
            ? await context.FarmerProfiles.AsNoTracking().Where(x => x.Id == farmerId).Select(x => x.User.Email).SingleOrDefaultAsync(token)
            : null;
        var summary = await payments.GetOrderSummaryAsync(orderId, null, token);
        var snapshot = new InvoiceSnapshot(1, new(store.Name,
            string.Join(", ", new[] { store.AddressLine, store.Ward, store.District, store.Province }.Where(x => !string.IsNullOrWhiteSpace(x))),
            store.PhoneNumber, store.Email, store.TaxCode), cashier, OrderQueries.ToResponse(order), summary);
        var issuedAt = clock.UtcNow;
        var number = await DocumentNumbers.NextAsync(context.SalesInvoices.AsNoTracking().Where(x => x.StoreId == storeId).Select(x => x.InvoiceNumber),
            "INV", BusinessCalendar.Today(issuedAt), token);
        token.ThrowIfCancellationRequested();
        var pdf = renderer.Render(number, issuedAt, snapshot);
        token.ThrowIfCancellationRequested();
        var invoice = new SalesInvoice(storeId, orderId, Actor, number, order.OrderNumber, order.CustomerNameSnapshot,
            email, order.TotalAmount, issuedAt, JsonSerializer.Serialize(snapshot, Json), pdf);
        context.SalesInvoices.Add(invoice);
        audit.Record("INVOICE_ISSUED", "INVOICE", invoice.Id, storeId, newValues: new { number, orderId, order.TotalAmount });
        // The unique order/number indexes handle competing first issuances; the shared handler returns 409.
        await context.SaveChangesAsync(token);
        return new(ToResponse(invoice, snapshot), true);
    }

    public async Task<PagedResult<InvoiceListItem>> ListAsync(InvoiceListRequest request, CancellationToken token)
    {
        var storeId = await ActiveStore.GetIdAsync(context, token);
        var query = context.SalesInvoices.AsNoTracking().Where(x => x.StoreId == storeId);
        if (request.OrderId is { } orderId) query = query.Where(x => x.OrderId == orderId);
        if (request.FromDate is { } from)
        {
            var start = BusinessCalendar.StartOfDay(from);
            query = query.Where(x => x.IssuedAt >= start);
        }
        if (request.ToDate is { } to)
        {
            var end = BusinessCalendar.StartOfDay(to).AddDays(1);
            query = query.Where(x => x.IssuedAt < end);
        }
        if (request.MinAmount is { } min) query = query.Where(x => x.TotalAmount >= min);
        if (request.MaxAmount is { } max) query = query.Where(x => x.TotalAmount <= max);
        if (request.HasCustomerEmail is { } hasEmail)
            query = hasEmail ? query.Where(x => x.CustomerEmail != null && x.CustomerEmail.Trim() != "")
                : query.Where(x => x.CustomerEmail == null || x.CustomerEmail.Trim() == "");
        if (Texts.Clean(request.Search) is { } search)
        {
            var term = search.ToLower();
            query = query.Where(x => x.InvoiceNumber.ToLower().Contains(term) || x.OrderNumber.ToLower().Contains(term) || x.CustomerName.ToLower().Contains(term));
        }
        var filtered = query.Select(invoice => new
        {
            Invoice = invoice,
            EmailStatus = context.InvoiceEmails.AsNoTracking().Where(email => email.InvoiceId == invoice.Id)
                .OrderByDescending(email => email.CreatedAt).ThenByDescending(email => email.Id)
                .Select(email => email.Status).FirstOrDefault() ?? "NOT_SENT"
        });
        if (Texts.Clean(request.EmailStatus) is { } emailStatus)
        {
            var status = emailStatus.ToUpperInvariant();
            filtered = filtered.Where(x => x.EmailStatus == status);
        }
        var total = await filtered.LongCountAsync(token);
        var items = await filtered.OrderByDescending(x => x.Invoice.IssuedAt).ThenByDescending(x => x.Invoice.Id).Skip(request.Skip).Take(request.PageSize)
            .Select(x => new InvoiceListItem(x.Invoice.Id, x.Invoice.InvoiceNumber, x.Invoice.OrderId, x.Invoice.OrderNumber, x.Invoice.CustomerName,
                x.Invoice.CustomerEmail, x.Invoice.TotalAmount, x.Invoice.IssuedAt, x.EmailStatus)).ToListAsync(token);
        return new(items, request.Page, request.PageSize, total);
    }

    public async Task<InvoiceResponse> GetAsync(Guid id, CancellationToken token)
    {
        var storeId = await ActiveStore.GetIdAsync(context, token);
        // Avoid loading the PDF just to display an invoice.
        var row = await context.SalesInvoices.AsNoTracking().Where(x => x.Id == id && x.StoreId == storeId)
            .Select(x => new { x.Id, x.InvoiceNumber, x.OrderId, x.OrderNumber, x.CustomerName, x.CustomerEmail, x.TotalAmount, x.IssuedAt, x.SnapshotJson })
            .SingleOrDefaultAsync(token) ?? throw new NotFoundException("Invoice", id);
        return new(row.Id, row.InvoiceNumber, row.OrderId, row.OrderNumber, row.CustomerName, row.CustomerEmail, row.TotalAmount, row.IssuedAt,
            JsonSerializer.Deserialize<InvoiceSnapshot>(row.SnapshotJson, Json)!);
    }

    public async Task<InvoicePdf> PdfAsync(Guid id, CancellationToken token)
    {
        var storeId = await ActiveStore.GetIdAsync(context, token);
        return await context.SalesInvoices.AsNoTracking().Where(x => x.Id == id && x.StoreId == storeId)
            .Select(x => new InvoicePdf(x.InvoiceNumber + ".pdf", x.PdfContent)).SingleOrDefaultAsync(token) ?? throw new NotFoundException("Invoice", id);
    }

    public async Task<PagedResult<InvoiceEmailResponse>> EmailsAsync(Guid id, PaginationRequest request, CancellationToken token)
    {
        await GetAsync(id, token);
        var query = context.InvoiceEmails.AsNoTracking().Where(x => x.InvoiceId == id);
        var total = await query.LongCountAsync(token);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip(request.Skip).Take(request.PageSize)
            .Select(x => new InvoiceEmailResponse(x.Id, x.Recipient, x.Status, x.CreatedAt, x.SentAt, x.Error)).ToListAsync(token);
        return new(rows, request.Page, request.PageSize, total);
    }

    public async Task<InvoiceEmailResponse> SendEmailAsync(Guid id, InvoiceEmailRequest request, CancellationToken token)
    {
        var invoice = await GetAsync(id, token);
        var existing = await context.InvoiceEmails.AsNoTracking().SingleOrDefaultAsync(x => x.InvoiceId == id && x.RequestId == request.RequestId, token);
        if (existing is not null)
        {
            if (!string.Equals(existing.Recipient, request.Recipient, StringComparison.OrdinalIgnoreCase))
                throw new ConflictException("Mã yêu cầu đã được dùng cho địa chỉ email khác.");
            return EmailResponse(existing);
        }
        if (!sender.IsConfigured) throw new InvoiceEmailUnavailableException();
        var storeId = await ActiveStore.GetIdAsync(context, token);
        var file = await PdfAsync(id, token);
        var attempt = new InvoiceEmail(id, Actor, request.RequestId, request.Recipient);
        context.InvoiceEmails.Add(attempt);
        await context.SaveChangesAsync(token); // Commit idempotency BEFORE sending anything externally.
        try
        {
            await sender.SendAsync(attempt.Recipient, invoice.InvoiceNumber, invoice.CustomerName, file.Content, token);
            attempt.Complete(clock.UtcNow);
        }
        catch (InvoiceEmailUnavailableException) { attempt.Fail(); }
        catch (OperationCanceledException)
        {
            attempt.Fail(interrupted: true);
            // Preserve the ambiguous outcome even when the browser has disconnected. No automatic resend.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await context.SaveChangesAsync(cleanup.Token);
            if (token.IsCancellationRequested) throw;
            return EmailResponse(attempt);
        }
        audit.Record("INVOICE_EMAIL_" + attempt.Status, "INVOICE", id, storeId, newValues: new { attempt.Id, attempt.Status });
        using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await context.SaveChangesAsync(completion.Token);
        return EmailResponse(attempt);
    }

    private static InvoiceResponse ToResponse(SalesInvoice x, InvoiceSnapshot snapshot) =>
        new(x.Id, x.InvoiceNumber, x.OrderId, x.OrderNumber, x.CustomerName, x.CustomerEmail, x.TotalAmount, x.IssuedAt, snapshot);
    private static InvoiceEmailResponse EmailResponse(InvoiceEmail x) => new(x.Id, x.Recipient, x.Status, x.CreatedAt, x.SentAt, x.Error);
}
