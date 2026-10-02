using AgriSage.Application.Common.Interfaces;

namespace AgriSage.Application.Features.Files;

public sealed class DeliveryProofService(IFileStorageService storage, IDateTimeProvider clock) : IDeliveryProofService
{
    public Task<UploadedImageResponse> UploadAsync(Stream content, CancellationToken cancellationToken) =>
        ImageUploader.UploadAsync(storage, clock, StorageArea.DeliveryProofs, ImageRules.MaxProofBytes, content, cancellationToken);

    public Task DeleteAsync(string? storageKey, CancellationToken cancellationToken) =>
        ImageUploader.DeleteAsync(
            storage, StorageArea.DeliveryProofs, storageKey, "The key is not an uploaded delivery photo.", cancellationToken);
}
