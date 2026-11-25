using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Invoices;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Invoices.Entities;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AgriSage.IntegrationTests.Infrastructure.Invoices;

public class InvoiceFilterDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Clock : IDateTimeProvider { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Pdf : IInvoicePdfRenderer
    {
        public byte[] Render(string invoiceNumber, DateTimeOffset issuedAt, InvoiceSnapshot snapshot) => throw new NotSupportedException();
    }
    private sealed class Sender : IInvoiceEmailSender
    {
        public bool IsConfigured => false;
        public Task SendAsync(string recipient, string invoiceNumber, string customerName, byte[] pdf, CancellationToken token) => throw new NotSupportedException();
    }

    // All fixtures/migrations live in a transaction-local schema and are rolled back; public data stays unchanged.
    [RealDbFact]
    public async Task Filters_use_Vietnam_days_inclusive_amounts_latest_email_attempt_and_store_scope_before_paging()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var db = session.NewContext();
        var schema = "invoice_filter_test_" + Guid.NewGuid().ToString("N");
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA " + schema + "; SET LOCAL search_path TO " + schema + ";", Token);
        var script = db.GetService<IMigrator>().GenerateScript(null, null, MigrationsSqlGenerationOptions.NoTransactions);
        await db.Database.ExecuteSqlRawAsync(script, Token);
        var (user, store, _) = await session.SeedAsync(db);
        var otherStore = new Store("OTHER", "Other store", "Address", "Province", status: StoreStatus.Inactive);
        db.Stores.Add(otherStore);
        var day = new DateOnly(2026, 11, 23);
        var start = BusinessCalendar.StartOfDay(day);
        var clock = new Clock();
        var service = new InvoiceService(db, session.CurrentUser, clock, new PaymentQueries(db), new Pdf(), new Sender(), new AuditTrail(db, session.CurrentUser, clock));

        SalesInvoice Add(int number, DateTimeOffset at, decimal amount, string? email, Guid? storeId = null)
        {
            var order = new Order(storeId ?? store.Id, "OD-FILTER-" + number, OrderSource.Counter, CustomerType.WalkIn,
                user.Id, "Invoice customer " + number, SettlementType.FullPayment, FulfillmentType.Pickup);
            var invoice = new SalesInvoice(storeId ?? store.Id, order.Id, user.Id, "INV-FILTER-" + number, order.OrderNumber,
                order.CustomerNameSnapshot, email, amount, at, "{}", [1]);
            db.AddRange(order, invoice);
            return invoice;
        }
        Add(1, start.AddMilliseconds(-1), 50, null);
        var first = Add(2, start, 100, null);
        var middle = Add(3, start.AddHours(12), 200, "customer@example.test");
        var last = Add(4, start.AddDays(1).AddMilliseconds(-1), 300, "   ");
        var next = Add(5, start.AddDays(1), 400, "customer@example.test");
        var pending = Add(6, start.AddHours(15), 350, "customer@example.test");
        var other = Add(7, start.AddHours(12), 200, "customer@example.test", otherStore.Id);
        await db.SaveChangesAsync(Token);
        var oldFailure = new InvoiceEmail(middle.Id, user.Id, Guid.NewGuid(), "customer@example.test"); oldFailure.Fail();
        db.InvoiceEmails.Add(oldFailure); await db.SaveChangesAsync(Token);
        var sent = new InvoiceEmail(middle.Id, user.Id, Guid.NewGuid(), "customer@example.test"); sent.Complete(clock.UtcNow);
        var failed = new InvoiceEmail(last.Id, user.Id, Guid.NewGuid(), "manual@example.test"); failed.Fail();
        var unknown = new InvoiceEmail(next.Id, user.Id, Guid.NewGuid(), "customer@example.test"); unknown.Fail(interrupted: true);
        var sending = new InvoiceEmail(pending.Id, user.Id, Guid.NewGuid(), "customer@example.test");
        var hidden = new InvoiceEmail(other.Id, user.Id, Guid.NewGuid(), "customer@example.test"); hidden.Complete(clock.UtcNow);
        db.AddRange(sent, failed, unknown, sending, hidden); await db.SaveChangesAsync(Token);

        var all = await service.ListAsync(new(), Token);
        Assert.Equal(6, all.TotalCount);
        Assert.DoesNotContain(all.Items, item => item.Id == other.Id);
        Assert.Equal("NOT_SENT", all.Items.Single(item => item.Id == first.Id).EmailStatus);
        Assert.Equal("SENT", all.Items.Single(item => item.Id == middle.Id).EmailStatus);
        var filter = new InvoiceListRequest { FromDate = day, ToDate = day, PageSize = 1 };
        var page1 = await service.ListAsync(filter, Token);
        var page2 = await service.ListAsync(filter with { Page = 2 }, Token);
        Assert.Equal(4, page1.TotalCount); Assert.Equal(4, page2.TotalCount); Assert.Equal(4, page1.TotalPages);
        Assert.NotEqual(Assert.Single(page1.Items).Id, Assert.Single(page2.Items).Id);
        Assert.Equal(5, (await service.ListAsync(new() { ToDate = day }, Token)).TotalCount); // Includes the previous day.
        Assert.Equal(5, (await service.ListAsync(new() { FromDate = day }, Token)).TotalCount);
        Assert.Equal(2, (await service.ListAsync(filter with { PageSize = 10, MaxAmount = 200 }, Token)).TotalCount);
        Assert.Equal(2, (await service.ListAsync(filter with { HasCustomerEmail = false }, Token)).TotalCount);
        Assert.Equal(2, (await service.ListAsync(filter with { HasCustomerEmail = true }, Token)).TotalCount);
        var combined = await service.ListAsync(filter with { MinAmount = 200, MaxAmount = 200, HasCustomerEmail = true, EmailStatus = "sent", Search = "OD-FILTER-3", OrderId = middle.OrderId }, Token);
        Assert.Equal(middle.Id, Assert.Single(combined.Items).Id);
        var failures = await service.ListAsync(new() { EmailStatus = "FAILED" }, Token);
        Assert.Equal(last.Id, Assert.Single(failures.Items).Id); // Historical failure on middle must not match.
        Assert.Equal(next.Id, Assert.Single((await service.ListAsync(new() { EmailStatus = "UNKNOWN" }, Token)).Items).Id);
        Assert.Equal(pending.Id, Assert.Single((await service.ListAsync(new() { EmailStatus = "SENDING" }, Token)).Items).Id);
        Assert.Equal(2, (await service.ListAsync(new() { EmailStatus = "NOT_SENT" }, Token)).TotalCount);
        Assert.Empty((await service.ListAsync(filter with { MinAmount = 500 }, Token)).Items);
    }
}
