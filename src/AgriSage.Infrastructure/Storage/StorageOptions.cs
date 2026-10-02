using AgriSage.Application.Common.Interfaces;

namespace AgriSage.Infrastructure.Storage;

// Supabase Storage settings ("Storage" section). Url and Bucket are not secret and are committed per environment;
// the secret key comes only from User Secrets (Storage:SecretKey) or the Storage__SecretKey environment variable.
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    // Project URL, e.g. https://<project-ref>.supabase.co
    public string? Url { get; init; }

    public string Bucket { get; init; } = "product-images";

    // Public bucket for delivery proof and incident photos (kept apart from catalog images).
    public string DeliveryProofBucket { get; init; } = "delivery-proofs";

    // Supabase secret key (sb_secret_...) or legacy service_role key. Server side only; never sent to clients.
    public string? SecretKey { get; init; }

    public bool IsConfigured =>
        Uri.TryCreate(Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrWhiteSpace(Bucket) && !string.IsNullOrWhiteSpace(SecretKey);

    public string BucketFor(StorageArea area) => area == StorageArea.DeliveryProofs ? DeliveryProofBucket : Bucket;
}
