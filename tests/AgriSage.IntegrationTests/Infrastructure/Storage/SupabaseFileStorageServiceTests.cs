using System.Net;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgriSage.IntegrationTests.Infrastructure.Storage;

// The Supabase adapter against a fake HTTP handler: what it sends, what it returns and how it fails. No network.
public class SupabaseFileStorageServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private const string Url = "https://abcdefgh.supabase.co";
    private const string SecretKey = "sb_secret_test_value_not_real";

    private sealed class RecordingHandler(
        HttpStatusCode status = HttpStatusCode.OK,
        Exception? failure = null,
        string body = "provider body that must never be shown") : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public byte[] Body { get; private set; } = [];

        public string? ContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            if (request.Content is not null)
            {
                Body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                ContentType = request.Content.Headers.ContentType?.MediaType;
            }

            return failure is null
                ? new HttpResponseMessage(status) { Content = new StringContent(body) }
                : throw failure;
        }
    }

    private static SupabaseFileStorageService Create(
        RecordingHandler handler,
        string? url = Url,
        string? key = SecretKey,
        string bucket = "product-images") =>
        new(new HttpClient(handler),
            Options.Create(new StorageOptions { Url = url, Bucket = bucket, SecretKey = key }),
            NullLogger<SupabaseFileStorageService>.Instance);

    private static FileUploadRequest Upload(byte[]? content = null) =>
        new(new MemoryStream(content ?? [1, 2, 3, 4]), "0123456789abcdef0123456789abcdef.png", "image/png", "2026/10");

    [Fact]
    public async Task Upload_posts_the_bytes_to_the_bucket_and_returns_the_public_url()
    {
        var handler = new RecordingHandler();

        var result = await Create(handler).UploadAsync(Upload([9, 8, 7]), Token);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(
            "https://abcdefgh.supabase.co/storage/v1/object/product-images/2026/10/0123456789abcdef0123456789abcdef.png",
            handler.Request.RequestUri!.ToString());
        Assert.Equal([9, 8, 7], handler.Body);
        Assert.Equal("image/png", handler.ContentType);
        Assert.Equal("false", handler.Request.Headers.GetValues("x-upsert").Single());
        Assert.Equal("2026/10/0123456789abcdef0123456789abcdef.png", result.StorageKey);
        Assert.Equal(
            "https://abcdefgh.supabase.co/storage/v1/object/public/product-images/2026/10/0123456789abcdef0123456789abcdef.png",
            result.Url);
        Assert.Equal(3, result.SizeBytes);
    }

    [Fact]
    public async Task New_secret_keys_go_in_the_apikey_header_only()
    {
        var handler = new RecordingHandler();

        await Create(handler).UploadAsync(Upload(), Token);

        Assert.Equal(SecretKey, handler.Request!.Headers.GetValues("apikey").Single());
        Assert.Null(handler.Request.Headers.Authorization);
    }

    [Fact]
    public async Task Legacy_jwt_keys_are_also_sent_as_the_bearer_token()
    {
        var handler = new RecordingHandler();
        const string jwt = "eyJhbGciOiJIUzI1NiJ9.e30.signature";

        await Create(handler, key: jwt).DeleteAsync("2026/10/x.png", Token);

        Assert.Equal(jwt, handler.Request!.Headers.GetValues("apikey").Single());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal(jwt, handler.Request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Delete_calls_the_object_endpoint_and_treats_a_missing_object_as_done()
    {
        var found = new RecordingHandler(HttpStatusCode.OK);
        var missing = new RecordingHandler(HttpStatusCode.NotFound);

        await Create(found).DeleteAsync("2026/10/0123456789abcdef0123456789abcdef.png", Token);
        await Create(missing).DeleteAsync("2026/10/0123456789abcdef0123456789abcdef.png", Token);

        Assert.Equal(HttpMethod.Delete, found.Request!.Method);
        Assert.Equal(
            "https://abcdefgh.supabase.co/storage/v1/object/product-images/2026/10/0123456789abcdef0123456789abcdef.png",
            found.Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Supabase_reports_a_missing_object_as_400_not_found_and_that_is_not_an_error()
    {
        const string notFound = "{\"statusCode\":\"404\",\"error\":\"not_found\",\"message\":\"Object not found\"}";

        await Create(new RecordingHandler(HttpStatusCode.BadRequest, body: notFound)).DeleteAsync("2026/10/x.png", Token);

        // Any other 400 is still an error.
        await Assert.ThrowsAsync<StorageUnavailableException>(() =>
            Create(new RecordingHandler(HttpStatusCode.BadRequest, body: "{\"error\":\"invalid_request\"}")).DeleteAsync("2026/10/x.png", Token));
        // A not-found answer to an upload is never accepted.
        await Assert.ThrowsAsync<StorageUnavailableException>(() =>
            Create(new RecordingHandler(HttpStatusCode.BadRequest, body: notFound)).UploadAsync(Upload(), Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Provider_errors_become_a_safe_unavailable_error(HttpStatusCode status)
    {
        var upload = await Assert.ThrowsAsync<StorageUnavailableException>(() =>
            Create(new RecordingHandler(status)).UploadAsync(Upload(), Token));
        var delete = await Assert.ThrowsAsync<StorageUnavailableException>(() =>
            Create(new RecordingHandler(status)).DeleteAsync("2026/10/x.png", Token));

        foreach (var message in new[] { upload.Message, delete.Message })
        {
            Assert.DoesNotContain("provider body", message);
            Assert.DoesNotContain(SecretKey, message);
            Assert.DoesNotContain("supabase", message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Network_failures_and_timeouts_become_an_unavailable_error()
    {
        await Assert.ThrowsAsync<StorageUnavailableException>(() =>
            Create(new RecordingHandler(failure: new HttpRequestException("connection refused to abcdefgh.supabase.co")))
                .UploadAsync(Upload(), Token));
        await Assert.ThrowsAsync<StorageUnavailableException>(() =>
            Create(new RecordingHandler(failure: new TaskCanceledException("timeout"))).UploadAsync(Upload(), Token));
    }

    [Fact]
    public async Task A_cancelled_request_is_not_reported_as_unavailable()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Create(new RecordingHandler(failure: new TaskCanceledException("cancelled", null, cancelled.Token)))
                .UploadAsync(Upload(), cancelled.Token));
    }

    [Theory]
    [InlineData(null, "key")]
    [InlineData("https://abcdefgh.supabase.co", null)]
    [InlineData("https://abcdefgh.supabase.co", "")]
    [InlineData("http://abcdefgh.supabase.co", "key")]
    [InlineData("not a url", "key")]
    [InlineData("", "key")]
    public async Task Missing_or_unsafe_configuration_fails_without_calling_the_provider(string? url, string? key)
    {
        var handler = new RecordingHandler();

        await Assert.ThrowsAsync<StorageUnavailableException>(() => Create(handler, url, key).UploadAsync(Upload(), Token));
        await Assert.ThrowsAsync<StorageUnavailableException>(() => Create(handler, url, key).DeleteAsync("a/b.png", Token));

        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task Keys_with_special_characters_are_escaped_per_segment()
    {
        var handler = new RecordingHandler();

        var result = await Create(handler, bucket: "my bucket").UploadAsync(
            new FileUploadRequest(new MemoryStream([1]), "a b.png", "image/png", "2026/10"), Token);

        Assert.Contains("/my%20bucket/2026/10/a%20b.png", handler.Request!.RequestUri!.AbsoluteUri);
        Assert.Contains("/public/my%20bucket/2026/10/a%20b.png", result.Url);
    }
}
