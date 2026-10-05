using System.Net;
using System.Runtime.CompilerServices;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Files;
using AgriSage.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgriSage.IntegrationTests.Infrastructure.Storage;

// [Fact] skipped unless AGRISAGE_STORAGE_TESTS=1 (and Storage:SecretKey is in User Secrets / Storage__SecretKey).
public sealed class RealStorageFactAttribute : FactAttribute
{
    public RealStorageFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (Environment.GetEnvironmentVariable("AGRISAGE_STORAGE_TESTS") != "1")
        {
            Skip = "Real Supabase Storage test; set AGRISAGE_STORAGE_TESTS=1 (and Storage:SecretKey) to run.";
        }
    }
}

// REAL Supabase Storage (agrisage-dev): uploads one 1x1 PNG, reads it back through the public URL, deletes it and
// checks it is gone. The object is removed even when an assertion fails.
public class RealStorageTests
{
    private sealed class NoUsage : IProofPhotoUsage
    {
        public Task<bool> IsUsedAsync(string storageKey, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static StorageOptions LoadOptions()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AgriSage.sln")))
        {
            root = root.Parent;
        }

        var api = Path.Combine(root!.FullName, "src", "AgriSage.Api");
        var secrets = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "UserSecrets", "6846d3dd-c916-4f07-a77a-fc69a19797d2", "secrets.json");

        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(api, "appsettings.json"))
            .AddJsonFile(Path.Combine(api, "appsettings.Development.json"))
            .AddJsonFile(secrets, optional: true)
            .AddEnvironmentVariables()
            .Build()
            .GetSection(StorageOptions.SectionName).Get<StorageOptions>()!;
    }

    [RealStorageFact]
    public async Task Upload_is_publicly_readable_and_delete_removes_it()
    {
        var options = LoadOptions();
        Assert.True(options.IsConfigured, "Storage:Url, Storage:Bucket and Storage:SecretKey must be configured.");
        var storage = new SupabaseFileStorageService(
            new HttpClient(), Options.Create(options), NullLogger<SupabaseFileStorageService>.Instance);
        var images = new ProductImageService(storage, new AgriSage.Infrastructure.Services.DateTimeProvider());
        using var reader = new HttpClient();
        string? key = null;

        try
        {
            var uploaded = await images.UploadAsync(new MemoryStream(TinyPng), Token);
            key = uploaded.StorageKey;

            Assert.True(ImageRules.IsValidKey(key));
            Assert.StartsWith($"{options.Url!.TrimEnd('/')}/storage/v1/object/public/{options.Bucket}/", uploaded.Url);
            var download = await reader.GetAsync(uploaded.Url, Token);
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal("image/png", download.Content.Headers.ContentType?.MediaType);
            Assert.Equal(TinyPng, await download.Content.ReadAsByteArrayAsync(Token));

            await images.DeleteAsync(key, Token);

            // The public URL can keep answering for a while (CDN cache), so "gone" is checked at the origin with the key.
            using var origin = new HttpRequestMessage(
                HttpMethod.Get, $"{options.Url!.TrimEnd('/')}/storage/v1/object/authenticated/{options.Bucket}/{key}");
            origin.Headers.TryAddWithoutValidation("apikey", options.SecretKey);
            var gone = await reader.SendAsync(origin, Token);
            Assert.False(gone.IsSuccessStatusCode, "The object still exists at the origin after delete.");
            // Deleting again is not an error.
            await images.DeleteAsync(key, Token);
            key = null;
        }
        finally
        {
            if (key is not null)
            {
                await storage.DeleteAsync(key, StorageArea.ProductImages, CancellationToken.None);
            }
        }
    }

    // Needs the public bucket "delivery-proofs" to exist (Storage:DeliveryProofBucket); the test leaves it empty.
    [RealStorageFact]
    public async Task Delivery_photo_goes_to_its_own_bucket_and_can_be_deleted()
    {
        var options = LoadOptions();
        Assert.True(options.IsConfigured, "Storage:Url, Storage:Bucket and Storage:SecretKey must be configured.");
        var storage = new SupabaseFileStorageService(
            new HttpClient(), Options.Create(options), NullLogger<SupabaseFileStorageService>.Instance);
        var proofs = new DeliveryProofService(storage, new AgriSage.Infrastructure.Services.DateTimeProvider(), new NoUsage());
        using var reader = new HttpClient();
        string? key = null;

        try
        {
            var uploaded = await proofs.UploadAsync(new MemoryStream(TinyPng), Token);
            key = uploaded.StorageKey;
            // The returned URL is recognized as this bucket's photo (accepted by delivery attempts and incidents).
            Assert.Equal(key, storage.KeyFromPublicUrl(uploaded.Url, StorageArea.DeliveryProofs));
            Assert.Null(storage.KeyFromPublicUrl(uploaded.Url, StorageArea.ProductImages));

            Assert.StartsWith($"{options.Url!.TrimEnd('/')}/storage/v1/object/public/{options.DeliveryProofBucket}/", uploaded.Url);
            var download = await reader.GetAsync(uploaded.Url, Token);
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal(TinyPng, await download.Content.ReadAsByteArrayAsync(Token));

            await proofs.DeleteAsync(key, Token);
            key = null;
        }
        finally
        {
            if (key is not null)
            {
                await storage.DeleteAsync(key, StorageArea.DeliveryProofs, CancellationToken.None);
            }
        }
    }
}
