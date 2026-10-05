using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;

namespace AgriSage.Application.Features.Files;

// Whether a delivery photo is referenced by saved records. Such a photo can no longer be deleted (decision C-D3):
// attempts and incidents (F2.6), and refund proof photos (F4.5).
public interface IProofPhotoUsage
{
    Task<bool> IsUsedAsync(string storageKey, CancellationToken cancellationToken);
}

public sealed class DeliveryProofService(IFileStorageService storage, IDateTimeProvider clock, IProofPhotoUsage usage) : IDeliveryProofService
{
    public Task<UploadedImageResponse> UploadAsync(Stream content, CancellationToken cancellationToken) =>
        ImageUploader.UploadAsync(storage, clock, StorageArea.DeliveryProofs, ImageRules.MaxProofBytes, content, cancellationToken);

    public async Task DeleteAsync(string? storageKey, CancellationToken cancellationToken)
    {
        if (ImageRules.IsValidKey(storageKey) && await usage.IsUsedAsync(storageKey!, cancellationToken))
        {
            throw new BusinessRuleException("This photo is used by a refund or delivery record and cannot be deleted.");
        }

        await ImageUploader.DeleteAsync(
            storage, StorageArea.DeliveryProofs, storageKey, "The key is not an uploaded delivery photo.", cancellationToken);
    }
}
