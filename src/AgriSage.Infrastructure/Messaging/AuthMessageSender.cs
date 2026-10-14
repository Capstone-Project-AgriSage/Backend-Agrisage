using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Domain.Features.Identity.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgriSage.Infrastructure.Messaging;

// Transport only: purpose, recipient and token lifetime are owned by Application. The cost guards (allowed recipients,
// daily limit) are transport policy for real SMS and are optional collaborators so the sender can be built by hand.
public sealed class AuthMessageSender(HttpClient http, IOptions<MessageDeliveryOptions> options,
    SmsDailyBudget? budget = null, IDateTimeProvider? clock = null, ILogger<AuthMessageSender>? logger = null)
    : IAuthMessageSender
{
    public bool IsConfigured(AuthDeliveryChannel channel)
    {
        var o = options.Value;
        return channel == AuthDeliveryChannel.Email
            ? !string.IsNullOrWhiteSpace(o.SmtpHost) && MailAddress.TryCreate(o.FromEmail, out _)
            : SmsConfigured(o);
    }

    public async Task SendAsync(AuthMessage message, CancellationToken cancellationToken)
    {
        if (!IsConfigured(message.Channel)) throw new MessageDeliveryUnavailableException();
        var o = options.Value;
        var subject = message.Purpose == AuthChallengePurpose.PasswordReset ? "AgriSage: đặt lại mật khẩu" : "AgriSage: xác minh tài khoản";
        try
        {
            if (message.Channel == AuthDeliveryChannel.Email)
            {
                var text = $"{subject}\nMã xác nhận: {message.Token}\nHết hạn lúc {message.ExpiresAt:O}.\nNếu bạn không yêu cầu thao tác này, hãy bỏ qua tin nhắn.";
                using var mail = new MailMessage(new MailAddress(o.FromEmail!, o.FromName), new MailAddress(message.Destination))
                { Subject = subject, Body = text, IsBodyHtml = false };
                using var smtp = new SmtpClient(o.SmtpHost, o.SmtpPort)
                { EnableSsl = true, UseDefaultCredentials = false, Timeout = 30_000 };
                if (!string.IsNullOrWhiteSpace(o.SmtpUsername)) smtp.Credentials = new NetworkCredential(o.SmtpUsername, o.SmtpPassword);
                await smtp.SendMailAsync(mail, cancellationToken);
            }
            else
            {
                await SendSmsAsync(o, message, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is SmtpException or HttpRequestException or TaskCanceledException or FormatException)
        { throw new MessageDeliveryUnavailableException(); }
    }

    private async Task SendSmsAsync(MessageDeliveryOptions o, AuthMessage message, CancellationToken cancellationToken)
    {
        // Stored numbers are already 0xxxxxxxxx; normalising again keeps the allow-list and +84 conversion exact.
        var local = ContactNormalizer.TryNormalizePhone(message.Destination, out var normalized) ? normalized : null;
        if (o.SmsAllowedRecipients.Length > 0 && (local is null || !IsAllowed(o.SmsAllowedRecipients, local)))
        {
            logger?.LogWarning("SMS was not sent: the recipient is not in MessageDelivery:SmsAllowedRecipients.");
            throw new MessageDeliveryUnavailableException();
        }
        if (o.UsesAndroidGateway && local is null) throw new MessageDeliveryUnavailableException();
        if (budget is not null && !budget.TryTake(o.SmsDailyLimit))
        {
            logger?.LogWarning("SMS was not sent: MessageDelivery:SmsDailyLimit ({Limit}) is used up for today.", o.SmsDailyLimit);
            throw new MessageDeliveryUnavailableException();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, o.SmsGatewayUrl);
        if (o.UsesAndroidGateway)
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{o.SmsUsername}:{o.SmsApiKey}"));
            request.Headers.Authorization = new("Basic", credentials);
            request.Content = JsonContent.Create(new
            {
                textMessage = new { text = SmsText(message) },
                phoneNumbers = new[] { "+84" + local![1..] },
            });
        }
        else
        {
            request.Headers.Authorization = new("Bearer", o.SmsApiKey);
            request.Content = JsonContent.Create(new { to = message.Destination, message = SmsText(message) });
        }
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new MessageDeliveryUnavailableException();
    }

    // One GSM-7 segment where the code is a 6-digit OTP: ASCII only (diacritics would make it UCS-2, 70 characters per
    // segment) and no timestamp. A password-reset token is long, so that message simply spans more segments.
    private string SmsText(AuthMessage message)
    {
        var now = clock?.UtcNow ?? TimeProvider.System.GetUtcNow();
        var minutes = Math.Max(1, (int)Math.Ceiling((message.ExpiresAt - now).TotalMinutes));
        var what = message.Purpose == AuthChallengePurpose.PasswordReset ? "ma dat lai mat khau" : "ma xac minh";
        return $"AgriSage: {what} {message.Token}. Het han sau {minutes} phut. Khong chia se ma nay cho ai.";
    }

    private static bool SmsConfigured(MessageDeliveryOptions o)
    {
        if (!Uri.TryCreate(o.SmsGatewayUrl, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(o.SmsApiKey))
            return false;
        if (!o.UsesAndroidGateway) return uri.Scheme == Uri.UriSchemeHttps;
        // The phone's own server speaks plain HTTP on the LAN; accept that only for private-network addresses.
        return !string.IsNullOrWhiteSpace(o.SmsUsername)
            && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && IsPrivateHost(uri.Host)));
    }

    private static bool IsAllowed(string[] allowed, string local) =>
        allowed.Any(a => ContactNormalizer.TryNormalizePhone(a, out var n) && n == local);

    private static bool IsPrivateHost(string host)
    {
        host = host.Trim('[', ']');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(host, out var ip)) return false;
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (ip.GetAddressBytes()[0] & 0xFE) == 0xFC;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254);
    }
}
