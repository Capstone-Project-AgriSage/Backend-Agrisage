using System.ComponentModel.DataAnnotations;

namespace AgriSage.Infrastructure.Messaging;

public sealed class MessageDeliveryOptions
{
    public const string GenericSmsProvider = "Generic";
    public const string AndroidGatewayProvider = "AndroidGateway";

    public string? SmtpHost { get; init; }
    [Range(1, 65535)] public int SmtpPort { get; init; } = 587;
    public string? SmtpUsername { get; init; }
    public string? SmtpPassword { get; init; }
    public string? FromEmail { get; init; }
    public string FromName { get; init; } = "AgriSage";

    // Generic (default): POST {to, message} with a Bearer key to an HTTPS gateway.
    // AndroidGateway: the open-source "SMS Gateway for Android" app (capcom6) running on a phone, which sends through
    // its own SIM: POST {textMessage:{text}, phoneNumbers:["+84..."]} with Basic auth (SmsUsername + SmsApiKey as the
    // password). SmsGatewayUrl is then the app's /message address, e.g. http://192.168.1.20:8080/message (plain HTTP is
    // accepted only for private-network addresses) or the cloud server over HTTPS.
    [RegularExpression("^(|Generic|AndroidGateway)$", ErrorMessage = "SmsProvider must be Generic or AndroidGateway.")]
    public string SmsProvider { get; init; } = GenericSmsProvider;
    public string? SmsGatewayUrl { get; init; }
    public string? SmsUsername { get; init; }
    public string? SmsApiKey { get; init; }

    // Cost guards for real SMS. Empty list = any recipient; otherwise only these numbers receive a real SMS and any
    // other number is refused without sending. Limit 0 = unlimited; otherwise the most real SMS sent per Vietnam day
    // by this process (the counter is kept in memory and starts again at 0 when the API restarts).
    public string[] SmsAllowedRecipients { get; init; } = [];
    [Range(0, 100_000)] public int SmsDailyLimit { get; init; }

    public bool UsesAndroidGateway =>
        string.Equals(SmsProvider, AndroidGatewayProvider, StringComparison.OrdinalIgnoreCase);
}
