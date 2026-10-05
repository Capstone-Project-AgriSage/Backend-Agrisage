using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Files;

namespace AgriSage.Application.Features.Deliveries;

// Delivery photos are uploaded first (POST /api/files/delivery-proofs); requests then carry the returned URL. Only URLs of
// that bucket with a server-generated key are accepted (FLOW_2 §8).
public sealed class DeliveryProofUrls(IFileStorageService storage)
{
    public string? Accept(string? url, string field)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var trimmed = url.Trim();

        return ImageRules.IsValidKey(storage.KeyFromPublicUrl(trimmed, StorageArea.DeliveryProofs))
            ? trimmed
            : throw new BusinessRuleException(
                "Upload the photo with POST /api/files/delivery-proofs and send the returned url.",
                new Dictionary<string, string[]> { [field] = ["Not a delivery photo uploaded to this store."] });
    }
}
