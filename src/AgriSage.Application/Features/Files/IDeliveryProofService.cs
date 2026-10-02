namespace AgriSage.Application.Features.Files;

// Uploads a delivery proof or incident photo to the delivery-proofs bucket and returns its URL, to be saved as
// delivery_attempts.proof_image_url / delivery_incidents.evidence_image_url by the delivery use cases.
public interface IDeliveryProofService
{
    Task<UploadedImageResponse> UploadAsync(Stream content, CancellationToken cancellationToken);

    // The key must be one produced by an upload (see ImageRules.IsValidKey).
    Task DeleteAsync(string? storageKey, CancellationToken cancellationToken);
}
