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

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/files/product-images", Form(Png), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/files/product-images?key={ValidKey}", Token)).StatusCode);
        Assert.Empty(_factory.Storage.Uploads);
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

        var response = await client.DeleteAsync($"/api/files/product-images?key={Uri.EscapeDataString(key)}", Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Storage.Deleted);
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

    public sealed record UploadedFile(string FileName, string ContentType, string Folder, byte[] Bytes);

    public sealed class FakeStorage : IFileStorageService
    {
        public List<UploadedFile> Uploads { get; } = [];

        public List<string> Deleted { get; } = [];

        public bool Fail { get; set; }

        public Task<StoredFileResult> UploadAsync(FileUploadRequest request, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new AgriSage.Application.Common.Exceptions.StorageUnavailableException();
            }

            using var copy = new MemoryStream();
            request.Content.CopyTo(copy);
            Uploads.Add(new UploadedFile(request.FileName, request.ContentType, request.Folder, copy.ToArray()));
            var key = $"{request.Folder}/{request.FileName}";

            return Task.FromResult(new StoredFileResult(key, $"https://cdn.example.com/{key}", copy.Length));
        }

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            Deleted.Add(storageKey);
            return Task.CompletedTask;
        }
    }

    public sealed class FilesApiFactory : WebApplicationFactory<Program>
    {
        public FakeStorage Storage { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
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
