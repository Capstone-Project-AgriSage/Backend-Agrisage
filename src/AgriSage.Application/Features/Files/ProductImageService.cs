using AgriSage.Application.Common.Interfaces;
using ValidationException = AgriSage.Application.Common.Exceptions.ValidationException;

namespace AgriSage.Application.Features.Files;

public sealed class ProductImageService(IFileStorageService storage, IDateTimeProvider clock) : IProductImageService
{
    public async Task<UploadedImageResponse> UploadAsync(Stream content, CancellationToken cancellationToken)
    {
        var buffer = await ReadLimitedAsync(content, cancellationToken);
        if (buffer.Length == 0)
        {
            throw Invalid("file", "The file is empty.");
        }

        var type = ImageRules.Detect(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 16)))
            ?? throw Invalid("file", "Only JPEG, PNG or WebP images are accepted.");

        var now = clock.UtcNow;
        buffer.Position = 0;

        // The name is generated here: client names are never used (no overwrite, no path tricks).
        var result = await storage.UploadAsync(
            new FileUploadRequest(buffer, $"{Guid.NewGuid():N}{type.Extension}", type.ContentType, $"{now:yyyy}/{now:MM}"),
            cancellationToken);

        return new UploadedImageResponse(result.Url, result.StorageKey, result.SizeBytes);
    }

    public Task DeleteAsync(string? storageKey, CancellationToken cancellationToken) =>
        ImageRules.IsValidKey(storageKey)
            ? storage.DeleteAsync(storageKey!, cancellationToken)
            : throw Invalid("key", "The key is not an uploaded product image.");

    // Reads at most MaxBytes + 1 so an oversized body is rejected without buffering all of it.
    private static async Task<MemoryStream> ReadLimitedAsync(Stream content, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[81920];

        while (true)
        {
            var read = await content.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                return buffer;
            }

            buffer.Write(chunk, 0, read);
            if (buffer.Length > ImageRules.MaxBytes)
            {
                throw Invalid("file", $"The image must not be larger than {ImageRules.MaxBytes / (1024 * 1024)} MB.");
            }
        }
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
