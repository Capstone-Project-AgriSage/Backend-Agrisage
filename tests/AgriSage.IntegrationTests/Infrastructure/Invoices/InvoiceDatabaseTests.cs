using System.Text;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Invoices;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Invoices;

public class InvoiceDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Pdf : IInvoicePdfRenderer
    {
        public byte[] Render(string number, DateTimeOffset issuedAt, InvoiceSnapshot snapshot) => Encoding.UTF8.GetBytes("%PDF-test-" + number);
    }
    private sealed class Sender : IInvoiceEmailSender
    {
        public bool IsConfigured { get; set; } = true;
        public bool Fail { get; set; }
        public bool Interrupt { get; set; }
        public List<byte[]> Attachments { get; } = [];
        public Task SendAsync(string recipient, string invoiceNumber, string customerName, byte[] pdf, CancellationToken token)
        {
            Attachments.Add(pdf.ToArray());
            if (Interrupt) throw new OperationCanceledException();
            if (Fail) throw new InvoiceEmailUnavailableException();
            return Task.CompletedTask;
        }
    }
    private static InvoiceService Service(CreditTestDatabase env, AgriSageDbContext db, Sender sender) =>
        new(db, env.User, env.Clock, new PaymentQueries(db), new Pdf(), sender, new AuditTrail(db, env.User, env.Clock));

    [CreditDbFact]
    public async Task Concurrent_issuance_keeps_one_invoice_and_retry_returns_that_invoice()
    {
        await using var env = await CreditTestDatabase.StartAsync(Token);
        var order = await env.FulfillAsync(1, Token);
        async Task<InvoiceIssueResult?> Issue()
        {
            using var scope = env.Scope(); var db = scope.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            try { return await Service(env, db, new()).IssueAsync(order.Id, Token); }
            catch (DbUpdateException error) when (error.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return null; }
        }
        var results = await Task.WhenAll(Issue(), Issue());
        Assert.Single(results, x => x?.Created == true);
        using var final = env.Scope(); var context = final.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        Assert.Equal(1, await context.SalesInvoices.CountAsync(Token));
        var retried = await Service(env, context, new()).IssueAsync(order.Id, Token);
        Assert.False(retried.Created);
        Assert.Equal(results.First(x => x is not null)!.Invoice.Id, retried.Invoice.Id);
    }

    [CreditDbFact]
    public async Task Issuance_is_idempotent_and_saved_snapshot_and_PDF_survive_later_changes()
    {
        await using var env = await CreditTestDatabase.StartAsync(Token);
        var pending = await env.OrderAsync(1, Token);
        using var scope = env.Scope(); var db = scope.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var service = Service(env, db, new());
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.IssueAsync(pending.Id, Token));
        Assert.Empty(await db.SalesInvoices.ToListAsync(Token));
        var order = await env.FulfillAsync(1, Token);
        var stockRows = await db.StockMovements.CountAsync(Token); var debtRows = await db.DebtTransactions.CountAsync(Token);
        var issued = await service.IssueAsync(order.Id, Token);
        Assert.True(issued.Created);
        var repeated = await service.IssueAsync(order.Id, Token);
        Assert.False(repeated.Created); Assert.Equal(issued.Invoice.Id, repeated.Invoice.Id);
        Assert.StartsWith("INV-", issued.Invoice.InvoiceNumber);
        Assert.Equal(order.TotalAmount, issued.Invoice.Snapshot.Payments.RemainingToPay);
        var pdf = await service.PdfAsync(issued.Invoice.Id, Token);
        var store = await db.Stores.SingleAsync(Token);
        store.UpdateDetails("Changed store", null, null, null, "Changed address", null, null, "Changed province");
        var farmer = await db.Users.SingleAsync(x => x.Id == env.FarmerUserId, Token); farmer.UpdateProfile("Changed customer", null);
        await db.SaveChangesAsync(Token);
        var saved = await service.GetAsync(issued.Invoice.Id, Token);
        Assert.Equal("Test", saved.Snapshot.Store.Name); Assert.Equal(order.CustomerName, saved.CustomerName);
        Assert.Equal(pdf.Content, (await service.PdfAsync(saved.Id, Token)).Content);
        Assert.Single((await service.ListAsync(new() { Search = order.OrderNumber }, Token)).Items);
        Assert.Equal(stockRows, await db.StockMovements.CountAsync(Token)); Assert.Equal(debtRows, await db.DebtTransactions.CountAsync(Token));
        store.ChangeStatus(StoreStatus.Inactive); db.Stores.Add(new Store("SECOND", "Second", "Address", "Province")); await db.SaveChangesAsync(Token);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(saved.Id, Token));
        await Assert.ThrowsAsync<NotFoundException>(() => service.PdfAsync(saved.Id, Token));
        Assert.Empty((await service.ListAsync(new(), Token)).Items);
    }

    [CreditDbFact]
    public async Task Email_history_records_success_failure_interruption_and_never_resends_the_same_request()
    {
        await using var env = await CreditTestDatabase.StartAsync(Token);
        var order = await env.FulfillAsync(1, Token);
        using var scope = env.Scope(); var db = scope.ServiceProvider.GetRequiredService<AgriSageDbContext>(); var sender = new Sender();
        var service = Service(env, db, sender); var invoice = (await service.IssueAsync(order.Id, Token)).Invoice;
        var request = new InvoiceEmailRequest("customer@example.test", Guid.NewGuid());
        var first = await service.SendEmailAsync(invoice.Id, request, Token);
        Assert.Equal("SENT", first.Status); Assert.NotNull(first.SentAt);
        Assert.Equal(first.Id, (await service.SendEmailAsync(invoice.Id, request, Token)).Id); Assert.Single(sender.Attachments);
        Assert.Equal((await service.PdfAsync(invoice.Id, Token)).Content, sender.Attachments[0]);
        await Assert.ThrowsAsync<ConflictException>(() => service.SendEmailAsync(invoice.Id, request with { Recipient = "other@example.test" }, Token));
        sender.Fail = true;
        var failed = await service.SendEmailAsync(invoice.Id, request with { RequestId = Guid.NewGuid() }, Token);
        Assert.Equal("FAILED", failed.Status); Assert.Null(failed.SentAt);
        sender.Fail = false; sender.Interrupt = true;
        var interrupted = request with { RequestId = Guid.NewGuid() };
        Assert.Equal("UNKNOWN", (await service.SendEmailAsync(invoice.Id, interrupted, Token)).Status);
        Assert.Equal("UNKNOWN", (await service.SendEmailAsync(invoice.Id, interrupted, Token)).Status);
        var calls = sender.Attachments.Count;
        sender.IsConfigured = false;
        await Assert.ThrowsAsync<InvoiceEmailUnavailableException>(() => service.SendEmailAsync(invoice.Id, request with { RequestId = Guid.NewGuid() }, Token));
        Assert.Equal(calls, sender.Attachments.Count);
        var history = await service.EmailsAsync(invoice.Id, new() { PageSize = 2 }, Token);
        Assert.Equal(3, history.TotalCount); Assert.Equal(2, history.Items.Count); Assert.Equal(2, history.TotalPages);
    }
}
