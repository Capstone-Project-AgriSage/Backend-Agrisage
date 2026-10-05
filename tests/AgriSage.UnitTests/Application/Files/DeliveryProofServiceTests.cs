using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Files;

namespace AgriSage.UnitTests.Application.Files;

public class DeliveryProofServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    private sealed class FakeClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeStorage : IFileStorageService
    {
        public FileUploadRequest? Uploaded { get; private set; }

        public (string Key, StorageArea Area)? Deleted { get; private set; }

        public Task<StoredFileResult> UploadAsync(FileUploadRequest request, CancellationToken cancellationToken)
        {
            Uploaded = request;
            var key = $"{request.Folder}/{request.FileName}";

            return Task.FromResult(new StoredFileResult(key, $"https://cdn.example.com/{key}", request.Content.Length));
        }

        public Task DeleteAsync(string storageKey, StorageArea area, CancellationToken cancellationToken)
        {
            Deleted = (storageKey, area);
            return Task.CompletedTask;
        }

        public string? KeyFromPublicUrl(string url, StorageArea area) =>
            url.StartsWith("https://cdn.example.com/", StringComparison.Ordinal) ? url["https://cdn.example.com/".Length..] : null;
    }

    private sealed class FakeUsage(params string[] used) : IProofPhotoUsage
    {
        public Task<bool> IsUsedAsync(string storageKey, CancellationToken cancellationToken) =>
            Task.FromResult(used.Contains(storageKey));
    }

    private static (DeliveryProofService Service, FakeStorage Storage) Create(params string[] usedKeys)
    {
        var storage = new FakeStorage();
        return (new DeliveryProofService(storage, new FakeClock(), new FakeUsage(usedKeys)), storage);
    }

    // Decision C-D3: a photo saved on a delivery or refund record stays.
    [Fact]
    public async Task A_photo_used_as_proof_cannot_be_deleted()
    {
        const string key = "2026/10/0123456789abcdef0123456789abcdef.jpg";
        var (service, storage) = Create(key);

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.DeleteAsync(key, TestContext.Current.CancellationToken));
        Assert.Null(storage.Deleted);
    }

    private static MemoryStream Jpeg(int total) => new([.. JpegHeader, .. new byte[total - JpegHeader.Length]]);

    [Fact]
    public async Task Photos_go_to_the_delivery_proofs_area_with_a_generated_name()
    {
        var (service, storage) = Create();

        var response = await service.UploadAsync(Jpeg(1000), Token);

        Assert.Equal(StorageArea.DeliveryProofs, storage.Uploaded!.Area);
        Assert.Equal("image/jpeg", storage.Uploaded.ContentType);
        Assert.Equal("2026/10", storage.Uploaded.Folder);
        Assert.Matches(@"^[0-9a-f]{32}\.jpg$", storage.Uploaded.FileName);
        Assert.True(ImageRules.IsValidKey(response.StorageKey));
    }

    [Fact]
    public async Task Product_images_still_go_to_the_product_images_area()
    {
        var storage = new FakeStorage();

        await new ProductImageService(storage, new FakeClock()).UploadAsync(Jpeg(1000), Token);

        Assert.Equal(StorageArea.ProductImages, storage.Uploaded!.Area);
    }

    [Fact]
    public async Task The_limit_is_five_megabytes_for_photos()
    {
        var (service, _) = Create();

        await service.UploadAsync(Jpeg((int)ImageRules.MaxProofBytes), Token);
        var error = await Assert.ThrowsAsync<ValidationException>(() => service.UploadAsync(Jpeg((int)ImageRules.MaxProofBytes + 1), Token));

        Assert.Contains("file", error.Errors.Keys);
        Assert.Equal(5 * 1024 * 1024, ImageRules.MaxProofBytes);
    }

    [Fact]
    public async Task Empty_and_non_image_files_are_rejected()
    {
        var (service, storage) = Create();

        await Assert.ThrowsAsync<ValidationException>(() => service.UploadAsync(new MemoryStream(), Token));
        await Assert.ThrowsAsync<ValidationException>(() => service.UploadAsync(new MemoryStream("not an image at all"u8.ToArray()), Token));
        Assert.Null(storage.Uploaded);
    }

    [Fact]
    public async Task Delete_only_accepts_uploaded_keys_and_uses_the_proofs_area()
    {
        var (service, storage) = Create();
        const string key = "2026/10/0123456789abcdef0123456789abcdef.jpg";

        await Assert.ThrowsAsync<ValidationException>(() => service.DeleteAsync("../../etc/passwd", Token));
        await Assert.ThrowsAsync<ValidationException>(() => service.DeleteAsync(null, Token));
        Assert.Null(storage.Deleted);

        await service.DeleteAsync(key, Token);

        Assert.Equal((key, StorageArea.DeliveryProofs), storage.Deleted);
    }
}
