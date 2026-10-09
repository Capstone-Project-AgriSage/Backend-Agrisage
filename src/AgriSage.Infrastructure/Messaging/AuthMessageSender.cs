using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Domain.Features.Identity.Enums;
using Microsoft.Extensions.Options;

namespace AgriSage.Infrastructure.Messaging;

// Transport only: purpose, recipient and token lifetime are owned by Application.
public sealed class AuthMessageSender(HttpClient http, IOptions<MessageDeliveryOptions> options) : IAuthMessageSender
{
    public bool IsConfigured(AuthDeliveryChannel channel)
    {
        var o = options.Value;
        return channel == AuthDeliveryChannel.Email
            ? !string.IsNullOrWhiteSpace(o.SmtpHost) && MailAddress.TryCreate(o.FromEmail, out _)
            : Uri.TryCreate(o.SmsGatewayUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https"
                && !string.IsNullOrWhiteSpace(o.SmsApiKey);
    }

    public async Task SendAsync(AuthMessage message, CancellationToken cancellationToken)
    {
        if (!IsConfigured(message.Channel)) throw new MessageDeliveryUnavailableException();
        var o = options.Value;
        var subject = message.Purpose == AuthChallengePurpose.PasswordReset ? "AgriSage: đặt lại mật khẩu" : "AgriSage: xác minh tài khoản";
        var text = $"{subject}\nMã xác nhận: {message.Token}\nHết hạn lúc {message.ExpiresAt:O}.\nNếu bạn không yêu cầu thao tác này, hãy bỏ qua tin nhắn.";
        try
        {
            if (message.Channel == AuthDeliveryChannel.Email)
            {
                using var mail = new MailMessage(new MailAddress(o.FromEmail!, o.FromName), new MailAddress(message.Destination))
                { Subject = subject, Body = text, IsBodyHtml = false };
                using var smtp = new SmtpClient(o.SmtpHost, o.SmtpPort)
                { EnableSsl = true, UseDefaultCredentials = false, Timeout = 30_000 };
                if (!string.IsNullOrWhiteSpace(o.SmtpUsername)) smtp.Credentials = new NetworkCredential(o.SmtpUsername, o.SmtpPassword);
                await smtp.SendMailAsync(mail, cancellationToken);
            }
            else
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, o.SmsGatewayUrl);
                request.Headers.Authorization = new("Bearer", o.SmsApiKey);
                request.Content = JsonContent.Create(new { to = message.Destination, message = text });
                using var response = await http.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode) throw new MessageDeliveryUnavailableException();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is SmtpException or HttpRequestException or TaskCanceledException or FormatException)
        { throw new MessageDeliveryUnavailableException(); }
    }
}
