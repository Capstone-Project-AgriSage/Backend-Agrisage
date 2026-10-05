using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgriSage.IntegrationTests.Api;

// The upload API without a database or Supabase: storage and the account-status check are faked.
public class FilesHttpTests : IClassFixture<FilesHttpTests.FilesApiFactory>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private readonly FilesApiFactory _factory;

    public FilesHttpTests(FilesApiFactory factory)
    {
        _factory = factory;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private HttpClient ClientFor(string? role)
    {
        var client = _factory.CreateClient();
        if (role is not null)
        {
            var token = _factory.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role);
            client.DefaultRequestHeaders.Authorization = new("Bearer", token.Value);
        }

        return client;
    }

    private static MultipartFormDataContent Form(byte[] bytes, string fileName = "photo.png", string field = "file", string type = "image/png")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);

        return new MultipartFormDataContent { { file, field, fileName } };
    }

    private static string ValidKey => "2026/10/0123456789abcdef0123456789abcdef.png";

    [Fact]
    public async Task Upload_and_delete_need_a_token()
    {
        using var client = ClientFor(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/files/product-images", Form(Png), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync($"/api/files/product-images?key={ValidKey}", Token)).StatusCode);
    }

    [Theory]
    [InlineData("SALES_STAFF")]
    [InlineData("DELIVERY_STAFF")]
    [InlineData("FARMER")]
    public async Task Only_admin_and_store_owner_can_upload_or_delete(string role)
    {
        using var client = ClientFor(role);
        var uploadsBefore = _factory.Storage.Uploads.Count;

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/files/product-images", Form(Png), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/files/product-images?key={ValidKey}", Token)).StatusCode);
        Assert.Equal(uploadsBefore, _factory.Storage.Uploads.Count);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("STORE_OWNER")]
    public async Task A_valid_image_is_uploaded_and_its_url_returned(string role)
    {
        using var client = ClientFor(role);
        var before = _factory.Storage.Uploads.Count;

        // The client name and content type are ignored: the type comes from the bytes.
        var response = await client.PostAsync("/api/files/product-images", Form(Png, "../../evil.php", type: "text/html"), Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement;
        Assert.StartsWith("https://cdn.example.com/", body.GetProperty("url").GetString());
        Assert.Matches(@"^\d{4}/\d{2}/[0-9a-f]{32}\.png$", body.GetProperty("storageKey").GetString());
        Assert.Equal(Png.Length, body.GetProperty("sizeBytes").GetInt64());
        var upload = _factory.Storage.Uploads[before];
        Assert.Equal("image/png", upload.ContentType);
        Assert.DoesNotContain("evil", upload.FileName);
        Assert.Equal(Png, upload.Bytes);
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_rejected_before_reaching_storage()
    {
        using var client = ClientFor("ADMIN");
        var before = _factory.Storage.Uploads.Count;

        var response = await client.PostAsync(
            "/api/files/product-images", Form("<script>alert(1)</script>"u8.ToArray(), "photo.jpg", type: "image/jpeg"), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement
            .GetProperty("errors").TryGetProperty("file", out _));
        Assert.Equal(before, _factory.Storage.Uploads.Count);
    }

    [Fact]
    public async Task An_image_over_three_megabytes_is_rejected()
    {
        using var client = ClientFor("ADMIN");
        var big = new byte[3 * 1024 * 1024 + 1];
        Png.CopyTo(big, 0);

        var response = await client.PostAsync("/api/files/product-images", Form(big), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_file_field_is_a_bad_request()
    {
        using var client = ClientFor("ADMIN");

        var response = await client.PostAsync("/api/files/product-images", Form(Png, field: "other"), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("other/2026/10/0123456789abcdef0123456789abcdef.png")]
    [InlineData("2026/10/short.png")]
    public async Task Delete_rejects_keys_that_are_not_uploaded_images(string key)
    {
        using var client = ClientFor("STORE_OWNER");
        var deletedBefore = _factory.Storage.Deleted.Count;

        var response = await client.DeleteAsync($"/api/files/product-images?key={Uri.EscapeDataString(key)}", Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(deletedBefore, _factory.Storage.Deleted.Count);
    }

    [Fact]
    public async Task Delete_removes_an_uploaded_key_and_a_missing_key_is_a_bad_request()
    {
        using var client = ClientFor("ADMIN");

        var deleted = await client.DeleteAsync($"/api/files/product-images?key={ValidKey}", Token);
        var missing = await client.DeleteAsync("/api/files/product-images", Token);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Contains(ValidKey, _factory.Storage.Deleted);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task A_storage_outage_is_reported_as_service_unavailable_without_details()
    {
        using var client = ClientFor("ADMIN");
        _factory.Storage.Fail = true;
        try
        {
            var response = await client.PostAsync("/api/files/product-images", Form(Png), Token);
            var body = await response.Content.ReadAsStringAsync(Token);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.DoesNotContain("supabase", body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _factory.Storage.Fail = false;
        }
    }

    [Fact]
    public async Task Swagger_lists_the_upload_endpoints()
    {
        using var client = ClientFor(null);

        var paths = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token))
            .RootElement.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/api/files/product-images", out var item));
        Assert.True(item.TryGetProperty("post", out _));
        Assert.True(item.TryGetProperty("delete", out _));
    }

    [Fact]
    public async Task Delivery_photo_endpoints_need_a_token()
    {
        using var client = ClientFor(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/files/delivery-proofs", Form(Png), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync($"/api/files/delivery-proofs?key={ValidKey}", Token)).StatusCode);
    }

    [Fact]
    public async Task Farmers_cannot_upload_or_delete_delivery_photos()
    {
        using var client = ClientFor("FARMER");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/files/delivery-proofs", Form(Png), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/files/delivery-proofs?key={ValidKey}", Token)).StatusCode);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("STORE_OWNER")]
    [InlineData("SALES_STAFF")]
    [InlineData("DELIVERY_STAFF")]
    public async Task Every_staff_role_can_upload_a_delivery_photo_into_the_proofs_area(string role)
    {
        using var client = ClientFor(role);
        var before = _factory.Storage.Uploads.Count;

        var response = await client.PostAsync("/api/files/delivery-proofs", Form(Png, "../../evil.php", type: "text/html"), Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement;
        Assert.Matches(@"^\d{4}/\d{2}/[0-9a-f]{32}\.png$", body.GetProperty("storageKey").GetString());
        var upload = _factory.Storage.Uploads[before];
        Assert.Equal(StorageArea.DeliveryProofs, upload.Area);
        Assert.DoesNotContain("evil", upload.FileName);
    }

    [Fact]
    public async Task Delivery_photos_may_be_up_to_five_megabytes_but_not_more()
    {
        using var client = ClientFor("DELIVERY_STAFF");
        var four = new byte[4 * 1024 * 1024];
        var six = new byte[5 * 1024 * 1024 + 1];
        Png.CopyTo(four, 0);
        Png.CopyTo(six, 0);

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/files/delivery-proofs", Form(four), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/files/delivery-proofs", Form(six), Token)).StatusCode);
    }

    [Fact]
    public async Task A_non_image_is_rejected_as_a_delivery_photo()
    {
        using var client = ClientFor("DELIVERY_STAFF");
        var before = _factory.Storage.Uploads.Count;

        var response = await client.PostAsync("/api/files/delivery-proofs", Form("<html></html>"u8.ToArray(), "p.jpg", type: "image/jpeg"), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, _factory.Storage.Uploads.Count);
    }

    [Theory]
    [InlineData("SALES_STAFF")]
    [InlineData("DELIVERY_STAFF")]
    public async Task Only_admin_and_store_owner_delete_delivery_photos(string role)
    {
        using var client = ClientFor(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/files/delivery-proofs?key={ValidKey}", Token)).StatusCode);
    }

    [Fact]
    public async Task Admin_deletes_a_delivery_photo_from_the_proofs_area_and_bad_keys_are_rejected()
    {
        using var client = ClientFor("ADMIN");
        var deletedBefore = _factory.Storage.DeletedAreas.Count;

        var deleted = await client.DeleteAsync($"/api/files/delivery-proofs?key={ValidKey}", Token);
        var invalid = await client.DeleteAsync("/api/files/delivery-proofs?key=../../etc/passwd", Token);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(deletedBefore + 1, _factory.Storage.DeletedAreas.Count);
        Assert.Equal(StorageArea.DeliveryProofs, _factory.Storage.DeletedAreas[^1]);
    }

    [Fact]
    public async Task Product_image_uploads_stay_in_the_product_images_area()
    {
        using var client = ClientFor("ADMIN");
        var before = _factory.Storage.Uploads.Count;

        await client.PostAsync("/api/files/product-images", Form(Png), Token);

        Assert.Equal(StorageArea.ProductImages, _factory.Storage.Uploads[before].Area);
    }

    public sealed record UploadedFile(
        string FileName, string ContentType, string Folder, byte[] Bytes, StorageArea Area = StorageArea.ProductImages);

    public sealed class FakeStorage : IFileStorageService
    {
        public List<UploadedFile> Uploads { get; } = [];

        public List<string> Deleted { get; } = [];

        public List<StorageArea> DeletedAreas { get; } = [];

        public bool Fail { get; set; }

        public Task<StoredFileResult> UploadAsync(FileUploadRequest request, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new AgriSage.Application.Common.Exceptions.StorageUnavailableException();
            }

            using var copy = new MemoryStream();
            request.Content.CopyTo(copy);
            Uploads.Add(new UploadedFile(request.FileName, request.ContentType, request.Folder, copy.ToArray(), request.Area));
            var key = $"{request.Folder}/{request.FileName}";

            return Task.FromResult(new StoredFileResult(key, $"https://cdn.example.com/{key}", copy.Length));
        }

        public Task DeleteAsync(string storageKey, StorageArea area, CancellationToken cancellationToken)
        {
            Deleted.Add(storageKey);
            DeletedAreas.Add(area);
            return Task.CompletedTask;
        }

        public string? KeyFromPublicUrl(string url, StorageArea area) =>
            url.StartsWith("https://cdn.example.com/", StringComparison.Ordinal) ? url["https://cdn.example.com/".Length..] : null;
    }

    public sealed class FilesApiFactory : WebApplicationFactory<Program>
    {
        public FakeStorage Storage { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            // In-process tests do not require machine-wide Windows event log access.
            builder.UseSetting("Logging:EventLog:LogLevel:Default", "None");
            // Test-only key; real keys come from User Secrets / environment variables.
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-not-a-secret-000000");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFileStorageService>();
                services.AddSingleton<IFileStorageService>(Storage);
                services.RemoveAll<AgriSage.Application.Features.Auth.Interfaces.IUserAccessValidator>();
                services.AddSingleton<AgriSage.Application.Features.Auth.Interfaces.IUserAccessValidator>(new StaffHttpTests.FakeAccounts());
            });
        }
    }
}
