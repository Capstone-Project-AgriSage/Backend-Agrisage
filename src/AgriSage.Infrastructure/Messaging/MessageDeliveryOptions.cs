using System.ComponentModel.DataAnnotations;

namespace AgriSage.Infrastructure.Messaging;

public sealed class MessageDeliveryOptions
{
    public string? SmtpHost { get; init; }
    [Range(1, 65535)] public int SmtpPort { get; init; } = 587;
    public string? SmtpUsername { get; init; }
    public string? SmtpPassword { get; init; }
    public string? FromEmail { get; init; }
    public string FromName { get; init; } = "AgriSage";
    public string? SmsGatewayUrl { get; init; }
    public string? SmsApiKey { get; init; }
}
