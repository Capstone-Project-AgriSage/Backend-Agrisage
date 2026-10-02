using AgriSage.Application.Common.Interfaces;
using ValidationException = AgriSage.Application.Common.Exceptions.ValidationException;

namespace AgriSage.Application.Features.Files;

// Shared upload pipeline of the image services: bounded read, type from the file content (magic bytes), server
// generated name, so a client-supplied name or Content-Type is never trusted.
internal static class ImageUploader
{
    public static async Task<UploadedImageResponse> UploadAsync(
        IFileStorageService storage,
        IDateTimeProvider clock,
        StorageArea area,
        long maxBytes,
        Stream content,
        CancellationToken cancellationToken)
    {
        var buffer = await ReadLimitedAsync(content, maxBytes, cancellationToken);
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
            new FileUploadRequest(buffer, $"{Guid.NewGuid():N}{type.Extension}", type.ContentType, $"{now:yyyy}/{now:MM}", area),
            cancellationToken);

        return new UploadedImageResponse(result.Url, result.StorageKey, result.SizeBytes);
    }

    public static Task DeleteAsync(IFileStorageService storage, StorageArea area, string? storageKey, string notAnUploadMessage, CancellationToken cancellationToken) =>
        ImageRules.IsValidKey(storageKey)
            ? storage.DeleteAsync(storageKey!, area, cancellationToken)
            : throw Invalid("key", notAnUploadMessage);

    // Reads at most maxBytes + 1 so an oversized body is rejected without buffering all of it.
    private static async Task<MemoryStream> ReadLimitedAsync(Stream content, long maxBytes, CancellationToken cancellationToken)
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
            if (buffer.Length > maxBytes)
            {
                throw Invalid("file", $"The image must not be larger than {maxBytes / (1024 * 1024)} MB.");
            }
        }
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
