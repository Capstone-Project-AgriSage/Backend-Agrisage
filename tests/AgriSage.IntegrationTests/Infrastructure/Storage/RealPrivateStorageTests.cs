using System.Net;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgriSage.IntegrationTests.Infrastructure.Storage;

// REAL Supabase Storage (agrisage-dev), private diagnosis bucket: uploads one 1x1 PNG, checks that it is NOT readable
// without a signature, signs it, reads the bytes back through the signed URL and through ReadAsync, deletes it and
// checks it is gone. The object is removed even when an assertion fails. Skipped unless AGRISAGE_STORAGE_TESTS=1.
public class RealPrivateStorageTests
{
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
    public async Task A_private_photo_is_only_readable_through_a_signature_and_delete_removes_it()
    {
        var options = LoadOptions();
        Assert.True(options.IsConfigured, "Storage:Url, Storage:Bucket and Storage:SecretKey must be configured.");
        var storage = new SupabaseFileStorageService(
            new HttpClient(), Options.Create(options), NullLogger<SupabaseFileStorageService>.Instance);
        using var anonymous = new HttpClient();
        string? key = null;

        try
        {
            await using var content = new MemoryStream(TinyPng);
            var now = DateTimeOffset.UtcNow;
            var stored = await storage.UploadAsync(
                new FileUploadRequest(content, $"{Guid.NewGuid():N}.png", "image/png", $"{now:yyyy}/{now:MM}", StorageArea.DiagnosisImages),
                Token);
            key = stored.StorageKey;

            // Private: neither the stored address nor the would-be public address works without a signature.
            Assert.DoesNotContain("/public/", stored.Url);
            Assert.NotEqual(HttpStatusCode.OK, (await anonymous.GetAsync(stored.Url, Token)).StatusCode);
            var publicGuess = stored.Url.Replace("/object/", "/object/public/", StringComparison.Ordinal);
            Assert.NotEqual(HttpStatusCode.OK, (await anonymous.GetAsync(publicGuess, Token)).StatusCode);

            // Signed: readable by anyone who has the URL, until it expires.
            var signed = await storage.CreateSignedUrlAsync(key, StorageArea.DiagnosisImages, TimeSpan.FromMinutes(2), Token);
            Assert.Contains("token=", signed);
            var viaUrl = await anonymous.GetAsync(signed, Token);
            Assert.Equal(HttpStatusCode.OK, viaUrl.StatusCode);
            Assert.Equal(TinyPng, await viaUrl.Content.ReadAsByteArrayAsync(Token));

            // The server reads it with its own key (the AI is run again on a stored photo).
            Assert.Equal(TinyPng, await storage.ReadAsync(key, StorageArea.DiagnosisImages, Token));

            await storage.DeleteAsync(key, StorageArea.DiagnosisImages, Token);
            await Assert.ThrowsAsync<StorageUnavailableException>(() => storage.ReadAsync(key, StorageArea.DiagnosisImages, Token));
            key = null;
        }
        finally
        {
            if (key is not null)
            {
                await storage.DeleteAsync(key, StorageArea.DiagnosisImages, CancellationToken.None);
            }
        }
    }
}
