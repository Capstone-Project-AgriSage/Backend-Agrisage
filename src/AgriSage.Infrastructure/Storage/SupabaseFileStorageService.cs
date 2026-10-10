using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgriSage.Infrastructure.Storage;

// Supabase Storage through its REST API (server side, with the secret key). The bucket is public: the returned URL
// is the public object URL. Failures surface as StorageUnavailableException without provider details; only the
// HTTP status is logged (never keys, URLs with credentials or response bodies).
public sealed class SupabaseFileStorageService(
    HttpClient httpClient,
    IOptions<StorageOptions> options,
    ILogger<SupabaseFileStorageService> logger) : IFileStorageService, IPrivateFileStore
{
    public async Task<StoredFileResult> UploadAsync(FileUploadRequest request, CancellationToken cancellationToken)
    {
        var settings = RequireConfigured(request.Area);
        var bucket = settings.BucketFor(request.Area);
        var key = $"{request.Folder.Trim('/')}/{request.FileName}";

        using var message = new HttpRequestMessage(HttpMethod.Post, ObjectUri(settings, bucket, key));
        Authorize(message, settings);
        message.Headers.TryAddWithoutValidation("x-upsert", "false");
        message.Content = new StreamContent(request.Content);
        message.Content.Headers.ContentType = new MediaTypeHeaderValue(request.ContentType);

        using var response = await SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Storage upload was rejected with status {StatusCode}.", (int)response.StatusCode);
            throw new StorageUnavailableException();
        }

        var size = request.Content.CanSeek ? request.Content.Length : 0;

        // A private bucket has no public URL: the returned address is the authenticated object path, which only
        // identifies the object. Readers go through IPrivateFileStore (signed URL or bytes).
        var url = request.Area == StorageArea.DiagnosisImages
            ? ObjectUri(settings, bucket, key).ToString()
            : PublicUrl(settings, bucket, key);

        return new StoredFileResult(key, url, size);
    }

    public async Task<string> CreateSignedUrlAsync(
        string storageKey, StorageArea area, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        var settings = RequireConfigured(area);
        var bucket = settings.BucketFor(area);
        var seconds = (int)Math.Clamp(lifetime.TotalSeconds, 1, 7 * 24 * 3600);

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"{settings.Url!.TrimEnd('/')}/storage/v1/object/sign/{Uri.EscapeDataString(bucket)}/{EscapeKey(storageKey)}")
        {
            Content = JsonContent.Create(new { expiresIn = seconds })
        };
        Authorize(message, settings);

        using var response = await SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Storage signing was rejected with status {StatusCode}.", (int)response.StatusCode);
            throw new StorageUnavailableException();
        }

        string? signed;
        try
        {
            using var body = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            signed = body.RootElement.TryGetProperty("signedURL", out var upper) ? upper.GetString()
                : body.RootElement.TryGetProperty("signedUrl", out var lower) ? lower.GetString()
                : null;
        }
        catch (JsonException)
        {
            signed = null;
        }

        if (string.IsNullOrWhiteSpace(signed))
        {
            logger.LogWarning("Storage signing answered without a URL.");
            throw new StorageUnavailableException();
        }

        // The answer is a path relative to the storage API (with or without the /storage/v1 prefix).
        var baseUrl = settings.Url!.TrimEnd('/');

        return signed.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? signed
            : signed.StartsWith("/storage/v1", StringComparison.Ordinal) ? baseUrl + signed
            : $"{baseUrl}/storage/v1{(signed.StartsWith('/') ? signed : "/" + signed)}";
    }

    public async Task<byte[]> ReadAsync(string storageKey, StorageArea area, CancellationToken cancellationToken)
    {
        var settings = RequireConfigured(area);
        var bucket = settings.BucketFor(area);

        using var message = new HttpRequestMessage(HttpMethod.Get, ObjectUri(settings, bucket, storageKey));
        Authorize(message, settings);

        using var response = await SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Storage read was rejected with status {StatusCode}.", (int)response.StatusCode);
            throw new StorageUnavailableException();
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task DeleteAsync(string storageKey, StorageArea area, CancellationToken cancellationToken)
    {
        var settings = RequireConfigured(area);
        var bucket = settings.BucketFor(area);

        using var message = new HttpRequestMessage(HttpMethod.Delete, ObjectUri(settings, bucket, storageKey));
        Authorize(message, settings);

        using var response = await SendAsync(message, cancellationToken);

        // Deleting an object that is already gone is not an error.
        if (!response.IsSuccessStatusCode && !await IsNotFoundAsync(response, cancellationToken))
        {
            logger.LogWarning("Storage delete was rejected with status {StatusCode}.", (int)response.StatusCode);
            throw new StorageUnavailableException();
        }
    }

    // Supabase reports a missing object either as HTTP 404 or as HTTP 400 with {"error":"not_found"} in the body.
    private static async Task<bool> IsNotFoundAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return true;
        }

        if (response.StatusCode != HttpStatusCode.BadRequest)
        {
            return false;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return body.Contains("\"not_found\"", StringComparison.Ordinal) || body.Contains("NoSuchKey", StringComparison.Ordinal);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("Storage request failed (network error).");
            throw new StorageUnavailableException();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Storage request timed out.");
            throw new StorageUnavailableException();
        }
    }

    // Without configuration no URL can be recognized, so none is accepted.
    public string? KeyFromPublicUrl(string url, StorageArea area)
    {
        var settings = options.Value;
        var bucket = settings.BucketFor(area);
        if (string.IsNullOrWhiteSpace(settings.Url) || string.IsNullOrWhiteSpace(bucket))
        {
            return null;
        }

        var prefix = PublicUrl(settings, bucket, "");
        return url.StartsWith(prefix, StringComparison.Ordinal) && url.Length > prefix.Length
            ? Uri.UnescapeDataString(url[prefix.Length..])
            : null;
    }

    private StorageOptions RequireConfigured(StorageArea area)
    {
        var settings = options.Value;
        if (!settings.IsConfigured || string.IsNullOrWhiteSpace(settings.BucketFor(area)))
        {
            logger.LogError("File storage is not configured (Storage:Url, Storage:Bucket, Storage:SecretKey).");
            throw new StorageUnavailableException();
        }

        return settings;
    }

    // The new secret keys (sb_secret_...) are not JWTs and go in the apikey header only; legacy service_role keys are
    // JWTs and are also sent as the bearer token.
    private static void Authorize(HttpRequestMessage message, StorageOptions settings)
    {
        message.Headers.TryAddWithoutValidation("apikey", settings.SecretKey);
        if (settings.SecretKey!.StartsWith("eyJ", StringComparison.Ordinal))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.SecretKey);
        }
    }

    private static Uri ObjectUri(StorageOptions settings, string bucket, string key) =>
        new($"{settings.Url!.TrimEnd('/')}/storage/v1/object/{Uri.EscapeDataString(bucket)}/{EscapeKey(key)}");

    private static string PublicUrl(StorageOptions settings, string bucket, string key) =>
        $"{settings.Url!.TrimEnd('/')}/storage/v1/object/public/{Uri.EscapeDataString(bucket)}/{EscapeKey(key)}";

    private static string EscapeKey(string key) => string.Join('/', key.Split('/').Select(Uri.EscapeDataString));
}
