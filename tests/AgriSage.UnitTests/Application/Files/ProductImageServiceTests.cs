using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Files;

namespace AgriSage.UnitTests.Application.Files;

public class ProductImageServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];
    private static readonly byte[] WebpHeader = [.. "RIFF"u8, 0x24, 0x00, 0x00, 0x00, .. "WEBP"u8, .. "VP8 "u8];

    private sealed class FakeClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeStorage : IFileStorageService
    {
        public FileUploadRequest? Uploaded { get; private set; }

        public byte[] UploadedBytes { get; private set; } = [];

        public string? Deleted { get; private set; }

        public Task<StoredFileResult> UploadAsync(FileUploadRequest request, CancellationToken cancellationToken)
        {
            Uploaded = request;
            using var copy = new MemoryStream();
            request.Content.CopyTo(copy);
            UploadedBytes = copy.ToArray();
            var key = $"{request.Folder}/{request.FileName}";

            return Task.FromResult(new StoredFileResult(key, $"https://cdn.example.com/{key}", UploadedBytes.Length));
        }

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            Deleted = storageKey;
            return Task.CompletedTask;
        }
    }

    private static (ProductImageService Service, FakeStorage Storage) Create()
    {
        var storage = new FakeStorage();
        return (new ProductImageService(storage, new FakeClock()), storage);
    }

    private static MemoryStream Bytes(byte[] header, int total = 0) =>
        new([.. header, .. new byte[Math.Max(0, total - header.Length)]]);

    [Theory]
    [InlineData("jpeg", ".jpg", "image/jpeg")]
    [InlineData("png", ".png", "image/png")]
    [InlineData("webp", ".webp", "image/webp")]
    public async Task Valid_images_are_uploaded_with_a_generated_name_and_the_detected_type(string kind, string extension, string contentType)
    {
        var header = kind switch { "jpeg" => JpegHeader, "png" => PngHeader, _ => WebpHeader };
        var (service, storage) = Create();

        var response = await service.UploadAsync(Bytes(header, 1000), Token);

        Assert.Equal(contentType, storage.Uploaded!.ContentType);
        Assert.Equal("2026/10", storage.Uploaded.Folder);
        Assert.Matches($"^[0-9a-f]{{32}}{extension.Replace(".", "\\.")}$", storage.Uploaded.FileName);
        Assert.Equal(1000, storage.UploadedBytes.Length);
        Assert.Equal($"2026/10/{storage.Uploaded.FileName}", response.StorageKey);
        Assert.True(ImageRules.IsValidKey(response.StorageKey));
    }

    [Fact]
    public async Task Two_uploads_never_share_a_name()
    {
        var (service, _) = Create();

        var first = await service.UploadAsync(Bytes(PngHeader, 100), Token);
        var second = await service.UploadAsync(Bytes(PngHeader, 100), Token);

        Assert.NotEqual(first.StorageKey, second.StorageKey);
    }

    [Fact]
    public async Task Empty_files_are_rejected()
    {
        var (service, storage) = Create();

        var error = await Assert.ThrowsAsync<ValidationException>(() => service.UploadAsync(new MemoryStream(), Token));

        Assert.Contains("file", error.Errors.Keys);
        Assert.Null(storage.Uploaded);
    }

    [Theory]
    [InlineData("not an image, just text pretending to be a jpg")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'></svg>")]
    [InlineData("GIF89a")]
    [InlineData("<?php echo 1; ?>")]
    public async Task Content_that_is_not_jpeg_png_or_webp_is_rejected_whatever_the_name(string text)
    {
        var (service, storage) = Create();

        await Assert.ThrowsAsync<ValidationException>(() => service.UploadAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)), Token));

        Assert.Null(storage.Uploaded);
    }

    [Fact]
    public async Task Riff_files_that_are_not_webp_are_rejected()
    {
        var (service, _) = Create();
        byte[] wave = [.. "RIFF"u8, 0x24, 0x00, 0x00, 0x00, .. "WAVE"u8, 0x00, 0x00];

        await Assert.ThrowsAsync<ValidationException>(() => service.UploadAsync(new MemoryStream(wave), Token));
    }

    [Fact]
    public async Task Files_over_the_limit_are_rejected_and_the_limit_itself_is_accepted()
    {
        var (service, storage) = Create();

        await service.UploadAsync(Bytes(PngHeader, (int)ImageRules.MaxBytes), Token);
        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            service.UploadAsync(Bytes(PngHeader, (int)ImageRules.MaxBytes + 1), Token));

        Assert.Contains("file", error.Errors.Keys);
        Assert.Equal((int)ImageRules.MaxBytes, storage.UploadedBytes.Length);
    }

    [Theory]
    [InlineData("2026/10/0123456789abcdef0123456789abcdef.jpg", true)]
    [InlineData("2026/10/0123456789abcdef0123456789abcdef.png", true)]
    [InlineData("2026/10/0123456789abcdef0123456789abcdef.webp", true)]
    [InlineData("../2026/10/0123456789abcdef0123456789abcdef.jpg", false)]
    [InlineData("2026/10/../../secret.jpg", false)]
    [InlineData("2026/10/0123456789abcdef0123456789abcdef.gif", false)]
    [InlineData("2026/10/short.jpg", false)]
    [InlineData("other-bucket/2026/10/0123456789abcdef0123456789abcdef.jpg", false)]
    [InlineData("/2026/10/0123456789abcdef0123456789abcdef.jpg", false)]
    [InlineData("2026/10/0123456789ABCDEF0123456789ABCDEF.jpg", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_keys_made_by_an_upload_are_valid(string? key, bool valid) =>
        Assert.Equal(valid, ImageRules.IsValidKey(key));

    [Fact]
    public async Task Delete_only_accepts_uploaded_keys()
    {
        var (service, storage) = Create();
        const string key = "2026/10/0123456789abcdef0123456789abcdef.jpg";

        await Assert.ThrowsAsync<ValidationException>(() => service.DeleteAsync("../../etc/passwd", Token));
        await Assert.ThrowsAsync<ValidationException>(() => service.DeleteAsync(null, Token));
        Assert.Null(storage.Deleted);

        await service.DeleteAsync(key, Token);

        Assert.Equal(key, storage.Deleted);
    }

    [Fact]
    public void Detection_works_on_short_headers_without_throwing()
    {
        Assert.Null(ImageRules.Detect([]));
        Assert.Null(ImageRules.Detect([0xFF, 0xD8]));
        Assert.Null(ImageRules.Detect([0x89, 0x50, 0x4E]));
        Assert.Same(ImageRules.Jpeg, ImageRules.Detect([0xFF, 0xD8, 0xFF]));
    }
}
