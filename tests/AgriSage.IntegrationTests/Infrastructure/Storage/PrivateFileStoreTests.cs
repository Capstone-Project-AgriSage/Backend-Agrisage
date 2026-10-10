using System.Net;
using System.Text;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgriSage.IntegrationTests.Infrastructure.Storage;

// The private diagnosis bucket of the Supabase adapter, against a fake HTTP handler (no network).
public sealed class PrivateFileStoreTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));

            return respond(request);
        }
    }

    private static SupabaseFileStorageService Service(Handler handler, StorageOptions? options = null) => new(
        new HttpClient(handler),
        Options.Create(options ?? new StorageOptions { Url = "https://proj.supabase.co", SecretKey = "sb_secret_test" }),
        NullLogger<SupabaseFileStorageService>.Instance);

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public void The_diagnosis_area_has_its_own_private_bucket()
    {
        var options = new StorageOptions();

        Assert.Equal("diagnosis-images", options.BucketFor(StorageArea.DiagnosisImages));
        Assert.Equal("delivery-proofs", options.BucketFor(StorageArea.DeliveryProofs));
        Assert.Equal("product-images", options.BucketFor(StorageArea.ProductImages));
    }

    [Theory]
    [InlineData("/object/sign/diagnosis-images/2026/11/a.jpg?token=T", "https://proj.supabase.co/storage/v1/object/sign/diagnosis-images/2026/11/a.jpg?token=T")]
    [InlineData("/storage/v1/object/sign/diagnosis-images/2026/11/a.jpg?token=T", "https://proj.supabase.co/storage/v1/object/sign/diagnosis-images/2026/11/a.jpg?token=T")]
    [InlineData("https://other.example/x?token=T", "https://other.example/x?token=T")]
    public async Task A_signed_url_is_made_from_the_relative_path_the_storage_returns(string signed, string expected)
    {
        var handler = new Handler(_ => Json($$"""{"signedURL":"{{signed}}"}"""));

        var url = await Service(handler).CreateSignedUrlAsync(
            "2026/11/a.jpg", StorageArea.DiagnosisImages, TimeSpan.FromHours(1), Token);

        Assert.Equal(expected, url);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://proj.supabase.co/storage/v1/object/sign/diagnosis-images/2026/11/a.jpg", request.RequestUri!.ToString());
        Assert.Equal("""{"expiresIn":3600}""", Assert.Single(handler.Bodies));
        Assert.Equal("sb_secret_test", Assert.Single(request.Headers.GetValues("apikey")));
    }

    [Theory]
    [InlineData("""{"error":"x"}""", HttpStatusCode.OK)]
    [InlineData("not json", HttpStatusCode.OK)]
    [InlineData("""{"signedURL":""}""", HttpStatusCode.OK)]
    [InlineData("""{"message":"Object not found"}""", HttpStatusCode.NotFound)]
    public async Task A_signing_that_gives_no_url_is_reported_as_unavailable(string body, HttpStatusCode status)
    {
        var service = Service(new Handler(_ => Json(body, status)));

        await Assert.ThrowsAsync<StorageUnavailableException>(() =>
            service.CreateSignedUrlAsync("2026/11/a.jpg", StorageArea.DiagnosisImages, TimeSpan.FromHours(1), Token));
    }

    [Fact]
    public async Task The_bytes_of_a_stored_photo_are_read_with_the_secret_key()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) });

        var bytes = await Service(handler).ReadAsync("2026/11/a.jpg", StorageArea.DiagnosisImages, Token);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, bytes);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://proj.supabase.co/storage/v1/object/diagnosis-images/2026/11/a.jpg", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task A_missing_object_is_reported_as_unavailable()
    {
        var service = Service(new Handler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        await Assert.ThrowsAsync<StorageUnavailableException>(() =>
            service.ReadAsync("2026/11/missing.jpg", StorageArea.DiagnosisImages, Token));
    }

    [Fact]
    public async Task A_diagnosis_upload_never_returns_a_public_url()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        await using var content = new MemoryStream([0xFF, 0xD8, 0xFF, 1]);

        var stored = await Service(handler).UploadAsync(
            new FileUploadRequest(content, "a.jpg", "image/jpeg", "2026/11", StorageArea.DiagnosisImages), Token);

        Assert.Equal("2026/11/a.jpg", stored.StorageKey);
        Assert.DoesNotContain("/public/", stored.Url);
        Assert.Contains("/diagnosis-images/", stored.Url);
        var request = Assert.Single(handler.Requests);
        Assert.Contains("/storage/v1/object/diagnosis-images/2026/11/a.jpg", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task A_public_area_still_returns_the_public_url()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        await using var content = new MemoryStream([0xFF, 0xD8, 0xFF, 1]);

        var stored = await Service(handler).UploadAsync(
            new FileUploadRequest(content, "a.jpg", "image/jpeg", "2026/11", StorageArea.DeliveryProofs), Token);

        Assert.Contains("/object/public/delivery-proofs/", stored.Url);
    }

    [Fact]
    public async Task Without_configuration_nothing_is_called()
    {
        var handler = new Handler(_ => Json("{}"));
        var service = Service(handler, new StorageOptions());

        await Assert.ThrowsAsync<StorageUnavailableException>(() =>
            service.CreateSignedUrlAsync("2026/11/a.jpg", StorageArea.DiagnosisImages, TimeSpan.FromHours(1), Token));
        await Assert.ThrowsAsync<StorageUnavailableException>(() =>
            service.ReadAsync("2026/11/a.jpg", StorageArea.DiagnosisImages, Token));
        Assert.Empty(handler.Requests);
    }
}
