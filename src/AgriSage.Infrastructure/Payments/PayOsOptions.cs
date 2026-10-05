namespace AgriSage.Infrastructure.Payments;

// payOS settings ("PayOS" section, FLOW_2 §6.2). ReturnUrl, CancelUrl and LinkExpiryMinutes are not secret; ClientId,
// ApiKey and ChecksumKey come only from User Secrets / environment variables / appsettings.Local.json and are never logged.
public sealed class PayOsOptions
{
    public const string SectionName = "PayOS";

    public string? ClientId { get; init; }

    public string? ApiKey { get; init; }

    public string? ChecksumKey { get; init; }

    // Where payOS sends the buyer's browser after paying / cancelling (the frontend only reads orderCode and asks our API).
    public string? ReturnUrl { get; init; }

    public string? CancelUrl { get; init; }

    public int LinkExpiryMinutes { get; init; } = 30;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ChecksumKey)
        && Uri.TryCreate(ReturnUrl, UriKind.Absolute, out _) && Uri.TryCreate(CancelUrl, UriKind.Absolute, out _)
        && LinkExpiryMinutes > 0;
}
