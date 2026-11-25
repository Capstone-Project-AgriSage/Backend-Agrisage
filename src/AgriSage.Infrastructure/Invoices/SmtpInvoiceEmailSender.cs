using System.Net;
using System.Net.Mail;
using System.Text;
using AgriSage.Application.Features.Invoices;
using AgriSage.Infrastructure.Messaging;
using Microsoft.Extensions.Options;

namespace AgriSage.Infrastructure.Invoices;

public sealed class SmtpInvoiceEmailSender(IOptions<MessageDeliveryOptions> options) : IInvoiceEmailSender
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.SmtpHost) && MailAddress.TryCreate(options.Value.FromEmail, out _);
    public async Task SendAsync(string recipient, string invoiceNumber, string customerName, byte[] pdf, CancellationToken token)
    {
        if (!IsConfigured) throw new InvoiceEmailUnavailableException();
        var settings = options.Value;
        try
        {
            using var mail = new MailMessage(new MailAddress(settings.FromEmail!, settings.FromName), new MailAddress(recipient))
            {
                Subject = "AgriSage - Hóa đơn " + invoiceNumber,
                Body = $"Kính gửi {customerName},\n\nHóa đơn bán hàng {invoiceNumber} được đính kèm dưới dạng PDF.\nCảm ơn quý khách!",
                SubjectEncoding = Encoding.UTF8, BodyEncoding = Encoding.UTF8, IsBodyHtml = false,
            };
            mail.Attachments.Add(new Attachment(new MemoryStream(pdf, writable: false), invoiceNumber + ".pdf", "application/pdf"));
            using var smtp = new SmtpClient(settings.SmtpHost, settings.SmtpPort) { EnableSsl = true, UseDefaultCredentials = false, Timeout = 30_000 };
            if (!string.IsNullOrWhiteSpace(settings.SmtpUsername)) smtp.Credentials = new NetworkCredential(settings.SmtpUsername, settings.SmtpPassword);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            await smtp.SendMailAsync(mail, deadline.Token);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is SmtpException or FormatException or InvalidOperationException)
        { throw new InvoiceEmailUnavailableException(); }
    }
}
