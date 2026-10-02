namespace AgriSage.Application.Features.Files;

public sealed record UploadedImageResponse(string Url, string StorageKey, long SizeBytes);

// Uploads a product/brand image to object storage and returns its public URL, to be saved in
// products.image_url / brands.logo_url through the catalog API.
public interface IProductImageService
{
    Task<UploadedImageResponse> UploadAsync(Stream content, CancellationToken cancellationToken);

    // The key must be one produced by an upload (see ImageRules.IsValidKey).
    Task DeleteAsync(string? storageKey, CancellationToken cancellationToken);
}
