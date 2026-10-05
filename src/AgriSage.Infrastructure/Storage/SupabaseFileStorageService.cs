using System.Net;
using System.Net.Http.Headers;
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
    ILogger<SupabaseFileStorageService> logger) : IFileStorageService
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

        return new StoredFileResult(key, PublicUrl(settings, bucket, key), size);
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
