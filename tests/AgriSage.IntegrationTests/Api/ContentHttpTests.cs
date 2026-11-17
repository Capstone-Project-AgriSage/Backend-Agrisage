using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

public class ContentHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private HttpClient ClientFor(string? role)
    {
        var client = factory.CreateClient();
        if (role != null)
        {
            var access = factory.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role);
            client.DefaultRequestHeaders.Authorization = new("Bearer", access.Value);
        }
        return client;
    }

    [Fact]
    public async Task Public_articles_and_contact_form_reach_validation_without_login()
    {
        using var client = ClientFor(null);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/articles?page=0", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/contact-requests", new { }, Token)).StatusCode);
    }

    [Theory]
    [InlineData("/api/admin/articles")]
    [InlineData("/api/contact-requests")]
    [InlineData("/api/me/contact-requests")]
    public async Task Private_lists_require_authentication(string path)
    {
        using var client = ClientFor(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path, Token)).StatusCode);
    }

    [Theory]
    [InlineData("FARMER")]
    [InlineData("DELIVERY_STAFF")]
    [InlineData("SALES_STAFF")]
    public async Task Articles_management_is_limited_to_admin_and_owner(string role)
    {
        using var client = ClientFor(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/admin/articles", new { }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/articles", Token)).StatusCode);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("STORE_OWNER")]
    public async Task Managers_reach_article_validation(string role)
    {
        using var client = ClientFor(role);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/admin/articles", new { }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/admin/articles/{Guid.NewGuid()}/status", new { status = "PENDING" }, Token)).StatusCode);
    }

    [Fact]
    public async Task Article_requires_image_and_upload_checks_file_content()
    {
        using var manager = ClientFor("ADMIN");
        var response = await manager.PostAsJsonAsync("/api/admin/articles", new { title = "Title", slug = "slug", content = "Content" }, Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("thumbnailUrl", await response.Content.ReadAsStringAsync(Token));
        using var upload = new MultipartFormDataContent();
        upload.Add(new ByteArrayContent("not an image"u8.ToArray()), "file", "fake.jpg");
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync("/api/files/article-images", upload, Token)).StatusCode);
    }

    [Theory]
    [InlineData("FARMER")]
    [InlineData("SALES_STAFF")]
    [InlineData("DELIVERY_STAFF")]
    public async Task Article_image_upload_requires_article_management_role(string role)
    {
        using var client = ClientFor(role);
        using var upload = new MultipartFormDataContent();
        upload.Add(new ByteArrayContent("not an image"u8.ToArray()), "file", "fake.jpg");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/files/article-images", upload, Token)).StatusCode);
    }

    [Theory]
    [InlineData("FARMER")]
    [InlineData("DELIVERY_STAFF")]
    public async Task Other_users_cannot_read_or_change_incoming_contacts(string role)
    {
        using var client = ClientFor(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/contact-requests", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/contact-requests/{Guid.NewGuid()}", new { status = "RESOLVED", resolutionNote = "Reply" }, Token)).StatusCode);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("STORE_OWNER")]
    [InlineData("SALES_STAFF")]
    public async Task Staff_reach_contact_validation_and_resolution_requires_a_reply(string role)
    {
        using var client = ClientFor(role);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/contact-requests?page=0", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/contact-requests/{Guid.NewGuid()}", new { status = "RESOLVED" }, Token)).StatusCode);
    }

    [Fact]
    public async Task Farmer_history_reaches_validation_but_staff_cannot_access_it()
    {
        using var farmer = ClientFor("FARMER");
        using var staff = ClientFor("SALES_STAFF");
        Assert.Equal(HttpStatusCode.BadRequest, (await farmer.GetAsync("/api/me/contact-requests?pageSize=101", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/me/contact-requests", Token)).StatusCode);
    }

    [Fact]
    public async Task Swagger_exposes_both_content_flows()
    {
        using var client = ClientFor(null);
        using var json = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token));
        var paths = json.RootElement.GetProperty("paths");
        foreach (var path in new[] { "/api/articles", "/api/articles/{slug}", "/api/admin/articles", "/api/files/article-images", "/api/contact-requests", "/api/me/contact-requests" })
            Assert.True(paths.TryGetProperty(path, out _), path);
    }
}
