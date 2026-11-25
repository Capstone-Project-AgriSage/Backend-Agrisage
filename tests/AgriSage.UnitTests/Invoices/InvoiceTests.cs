using AgriSage.Application.Features.Invoices;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Invoices.Entities;

namespace AgriSage.UnitTests.Invoices;

public class InvoiceTests
{
    [Theory]
    [InlineData("bad-address")]
    [InlineData("A <a@example.test>")]
    [InlineData("a@example.test,b@example.test")]
    [InlineData("a@example.test\r\nBcc: other@example.test")]
    public void Recipient_is_exactly_one_valid_address(string recipient) =>
        Assert.False(new InvoiceEmailValidator().Validate(new InvoiceEmailRequest(recipient, Guid.NewGuid())).IsValid);

    [Fact]
    public void Recipient_and_idempotency_key_are_validated()
    {
        var validator = new InvoiceEmailValidator();
        Assert.True(validator.Validate(new InvoiceEmailRequest("khach@example.test", Guid.NewGuid())).IsValid);
        Assert.False(validator.Validate(new InvoiceEmailRequest("khach@example.test", Guid.Empty)).IsValid);
        Assert.False(new InvoiceListValidator().Validate(new InvoiceListRequest { Page = 0 }).IsValid);
        Assert.False(new InvoiceListValidator().Validate(new InvoiceListRequest { PageSize = 101 }).IsValid);
    }

    [Fact]
    public void Invoice_filters_accept_optional_inclusive_ranges_and_reject_invalid_values()
    {
        var validator = new InvoiceListValidator();
        var day = new DateOnly(2026, 11, 23);
        Assert.True(validator.Validate(new InvoiceListRequest { FromDate = day, ToDate = day, MinAmount = 0, MaxAmount = 0, EmailStatus = "sent", HasCustomerEmail = false }).IsValid);
        Assert.True(validator.Validate(new InvoiceListRequest { FromDate = day }).IsValid);
        Assert.True(validator.Validate(new InvoiceListRequest { ToDate = day, MaxAmount = 9999999999999999.99m }).IsValid);
        foreach (var request in new InvoiceListRequest[]
        {
            new() { FromDate = day, ToDate = day.AddDays(-1) },
            new() { FromDate = DateOnly.MinValue }, new() { ToDate = DateOnly.MaxValue },
            new() { MinAmount = -1 }, new() { MaxAmount = -1 }, new() { MinAmount = 0.001m },
            new() { MinAmount = 200, MaxAmount = 100 }, new() { MaxAmount = 10000000000000000m },
            new() { EmailStatus = "PAID" }, new() { OrderId = Guid.Empty }
        }) Assert.False(validator.Validate(request).IsValid);
        foreach (var status in new[] { "NOT_SENT", "SENDING", "SENT", "FAILED", "UNKNOWN" })
            Assert.True(validator.Validate(new InvoiceListRequest { EmailStatus = status }).IsValid);
    }
    [Fact]
    public void Email_outcomes_are_terminal_and_do_not_claim_ambiguous_sends_succeeded()
    {
        var sent = new InvoiceEmail(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "a@example.test");
        var now = DateTimeOffset.UtcNow;
        sent.Complete(now); Assert.Equal("SENT", sent.Status); Assert.Equal(now, sent.SentAt);
        Assert.Throws<DomainException>(() => sent.Fail());
        var interrupted = new InvoiceEmail(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "a@example.test");
        interrupted.Fail(true); Assert.Equal("UNKNOWN", interrupted.Status); Assert.Null(interrupted.SentAt);
        Assert.Throws<DomainException>(() => interrupted.Complete(now));
    }
    [Fact]
    public void Invoice_requires_pdf_and_copies_original_bytes()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var invoice = new SalesInvoice(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "INV-1", "OD-1", "Khách", null, 100, DateTimeOffset.UtcNow, "{}", bytes);
        bytes[0] = 99; Assert.Equal(1, invoice.PdfContent[0]);
        Assert.Throws<ArgumentException>(() => new SalesInvoice(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "INV-1", "OD-1", "Khách", null, 100, DateTimeOffset.UtcNow, "{}", []));
    }
}
