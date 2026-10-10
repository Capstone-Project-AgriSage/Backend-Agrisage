using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Diagnosis.Entities;
using Microsoft.Extensions.Logging;

namespace AgriSage.Application.Features.Diagnosis;

// Signed URLs of the photos of a case (D9). Created at read time and never stored; a photo that cannot be signed right
// now is left out of the answer instead of failing the whole case.
public sealed class DiagnosisImageUrls(IPrivateFileStore store, IDateTimeProvider clock, ILogger<DiagnosisImageUrls> logger)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public async Task<IReadOnlyList<DiagnosisImageResponse>> ForAsync(
        IEnumerable<DiagnosisImage> images, CancellationToken cancellationToken)
    {
        var expiresAt = clock.UtcNow.Add(Lifetime);
        var result = new List<DiagnosisImageResponse>();

        foreach (var image in images.Where(i => !i.IsDeleted).OrderByDescending(i => i.IsPrimary).ThenBy(i => i.UploadedAt))
        {
            try
            {
                var url = await store.CreateSignedUrlAsync(image.StorageKey, StorageArea.DiagnosisImages, Lifetime, cancellationToken);
                result.Add(new DiagnosisImageResponse(image.Id, url, expiresAt, image.IsPrimary, image.FileName, image.UploadedAt));
            }
            catch (StorageUnavailableException)
            {
                logger.LogWarning("A diagnosis photo could not be signed; it is left out of the response.");
            }
        }

        return result;
    }
}
