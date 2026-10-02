using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// Authorization and validation of the catalog API without a database (the account-status check is faked).
public class CatalogHttpTests : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private readonly StaffHttpTests.StaffApiFactory _factory;

    public CatalogHttpTests(StaffHttpTests.StaffApiFactory factory)
    {
        _factory = factory;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly string Id = Guid.NewGuid().ToString();

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

    // Every endpoint that changes data.
    public static TheoryData<string, string> WriteEndpoints => new()
    {
        { "POST", "/api/categories" },
        { "PUT", $"/api/categories/{Id}" },
        { "POST", $"/api/categories/{Id}/activate" },
        { "POST", $"/api/categories/{Id}/deactivate" },
        { "DELETE", $"/api/categories/{Id}" },
        { "POST", "/api/brands" },
        { "PUT", $"/api/brands/{Id}" },
        { "DELETE", $"/api/brands/{Id}" },
        { "POST", "/api/active-ingredients" },
        { "PUT", $"/api/active-ingredients/{Id}" },
        { "DELETE", $"/api/active-ingredients/{Id}" },
        { "POST", "/api/products" },
        { "PUT", $"/api/products/{Id}" },
        { "POST", $"/api/products/{Id}/status" },
        { "DELETE", $"/api/products/{Id}" },
        { "POST", $"/api/products/{Id}/packagings" },
        { "PUT", $"/api/products/{Id}/packagings/{Id}" },
        { "DELETE", $"/api/products/{Id}/packagings/{Id}" },
        { "PUT", $"/api/products/{Id}/ingredients" },
        { "POST", "/api/store-products" },
        { "PUT", $"/api/store-products/{Id}" },
        { "POST", $"/api/store-products/{Id}/mark-sellable" },
        { "POST", $"/api/store-products/{Id}/mark-not-sellable" },
        { "POST", $"/api/store-products/{Id}/activate" },
        { "POST", $"/api/store-products/{Id}/deactivate" },
        { "DELETE", $"/api/store-products/{Id}" }
    };

    // Every staff-facing read endpoint.
    public static TheoryData<string> ReadEndpoints =>
    [
        "/api/categories", "/api/categories/tree", $"/api/categories/{Id}",
        "/api/brands", $"/api/brands/{Id}",
        "/api/active-ingredients", $"/api/active-ingredients/{Id}",
        "/api/units",
        "/api/products", $"/api/products/{Id}",
        "/api/store-products", $"/api/store-products/{Id}"
    ];

    private static HttpRequestMessage Request(string method, string url) =>
        new(new HttpMethod(method), url) { Content = method == "GET" || method == "DELETE" ? null : JsonContent.Create(new { }) };

    [Theory]
    [MemberData(nameof(WriteEndpoints))]
    public async Task Writes_need_a_token(string method, string url)
    {
        using var client = ClientFor(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Request(method, url), Token)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(WriteEndpoints))]
    public async Task Only_admin_and_store_owner_can_write(string method, string url)
    {
        foreach (var role in new[] { "SALES_STAFF", "DELIVERY_STAFF", "FARMER" })
        {
            using var client = ClientFor(role);

            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Request(method, url), Token)).StatusCode);
        }
    }

    [Theory]
    [MemberData(nameof(ReadEndpoints))]
    public async Task Staff_reads_need_a_token_and_exclude_farmers(string url)
    {
        using var anonymous = ClientFor(null);
        using var farmer = ClientFor("FARMER");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await farmer.GetAsync(url, Token)).StatusCode);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("STORE_OWNER")]
    [InlineData("SALES_STAFF")]
    [InlineData("DELIVERY_STAFF")]
    public async Task All_staff_roles_pass_the_read_gate(string role)
    {
        using var client = ClientFor(role);

        // pageSize=0 fails validation (400) before any database access, which proves the role was allowed in.
        foreach (var url in new[] { "/api/categories", "/api/brands", "/api/active-ingredients", "/api/units", "/api/products", "/api/store-products" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{url}?pageSize=0", Token)).StatusCode);
        }
    }

    [Fact]
    public async Task Public_catalog_needs_no_token()
    {
        using var client = ClientFor(null);

        // Reaches validation (400) instead of 401, so no sign-in is required.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/catalog/products?pageSize=0", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/catalog/brands?pageSize=101", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/catalog/products?page=0", Token)).StatusCode);
    }

    [Fact]
    public async Task Create_product_with_bad_data_returns_field_errors()
    {
        using var client = ClientFor("STORE_OWNER");

        var response = await client.PostAsJsonAsync(
            "/api/products",
            new { sku = "", name = "Thuoc", categoryId = Guid.NewGuid(), imageUrl = "http://insecure/x.jpg", packagings = Array.Empty<object>() },
            Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("sku", out _));
        Assert.True(errors.TryGetProperty("imageUrl", out _));
        Assert.True(errors.TryGetProperty("packagings", out _));
    }

    [Fact]
    public async Task Packaging_without_a_valid_conversion_is_a_bad_request()
    {
        using var client = ClientFor("ADMIN");

        var response = await client.PostAsJsonAsync(
            $"/api/products/{Id}/packagings",
            new { unitId = Guid.NewGuid(), conversionToBase = 0, isBaseUnit = false, isPurchaseUnit = true, isSaleUnit = true },
            Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_packaging_status_must_be_active_or_inactive()
    {
        using var client = ClientFor("ADMIN");

        var response = await client.PutAsJsonAsync(
            $"/api/products/{Id}/packagings/{Id}",
            new { isPurchaseUnit = true, isSaleUnit = true, status = "DISCONTINUED" },
            Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_lists_the_catalog_endpoints()
    {
        using var client = ClientFor(null);

        var paths = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token))
            .RootElement.GetProperty("paths");

        foreach (var path in new[]
        {
            "/api/categories", "/api/categories/tree", "/api/categories/{id}", "/api/brands", "/api/active-ingredients",
            "/api/units", "/api/products", "/api/products/{id}/packagings", "/api/products/{id}/ingredients",
            "/api/products/{id}/status", "/api/store-products", "/api/store-products/{id}/mark-sellable",
            "/api/catalog/categories", "/api/catalog/brands", "/api/catalog/products", "/api/catalog/products/{id}"
        })
        {
            Assert.True(paths.TryGetProperty(path, out _), path);
        }
    }
}
