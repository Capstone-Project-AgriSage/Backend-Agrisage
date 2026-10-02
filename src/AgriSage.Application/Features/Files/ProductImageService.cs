using AgriSage.Application.Common.Interfaces;

namespace AgriSage.Application.Features.Files;

public sealed class ProductImageService(IFileStorageService storage, IDateTimeProvider clock) : IProductImageService
{
    public Task<UploadedImageResponse> UploadAsync(Stream content, CancellationToken cancellationToken) =>
        ImageUploader.UploadAsync(storage, clock, StorageArea.ProductImages, ImageRules.MaxBytes, content, cancellationToken);

    public Task DeleteAsync(string? storageKey, CancellationToken cancellationToken) =>
        ImageUploader.DeleteAsync(
            storage, StorageArea.ProductImages, storageKey, "The key is not an uploaded product image.", cancellationToken);
}
