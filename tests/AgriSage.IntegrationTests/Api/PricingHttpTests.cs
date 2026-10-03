using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// Price list endpoints (F1.1) without a database: roles, validation and Swagger.
public class PricingHttpTests : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private readonly StaffHttpTests.StaffApiFactory _factory;

    public PricingHttpTests(StaffHttpTests.StaffApiFactory factory)
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

    private static HttpRequestMessage Request(string method, string url) =>
        new(new HttpMethod(method), url) { Content = method is "GET" or "DELETE" ? null : JsonContent.Create(new { }) };

    public static TheoryData<string, string> ReadEndpoints => new()
    {
        { "GET", "/api/price-lists" },
        { "GET", $"/api/price-lists/{Id}" },
        { "GET", $"/api/price-lists/{Id}/items" }
    };

    public static TheoryData<string, string> ManageEndpoints => new()
    {
        { "POST", "/api/price-lists" },
        { "PUT", $"/api/price-lists/{Id}" },
        { "POST", $"/api/price-lists/{Id}/activate" },
        { "POST", $"/api/price-lists/{Id}/deactivate" },
        { "DELETE", $"/api/price-lists/{Id}" },
        { "PUT", $"/api/price-lists/{Id}/items" },
        { "DELETE", $"/api/price-lists/{Id}/items/{Id}" }
    };

    [Theory]
    [MemberData(nameof(ReadEndpoints))]
    [MemberData(nameof(ManageEndpoints))]
    public async Task Every_endpoint_needs_a_token_and_is_closed_to_delivery_staff_and_farmers(string method, string url)
    {
        using (var anonymous = ClientFor(null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Request(method, url), Token)).StatusCode);
        }

        foreach (var role in new[] { "DELIVERY_STAFF", "FARMER" })
        {
            using var client = ClientFor(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Request(method, url), Token)).StatusCode);
        }
    }

    [Theory]
    [MemberData(nameof(ManageEndpoints))]
    public async Task Sales_staff_read_prices_but_cannot_change_them(string method, string url)
    {
        using var client = ClientFor("SALES_STAFF");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Request(method, url), Token)).StatusCode);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("STORE_OWNER")]
    [InlineData("SALES_STAFF")]
    public async Task Staff_reach_validation_of_the_read_endpoints(string role)
    {
        using var client = ClientFor(role);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/price-lists?pageSize=0", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/price-lists?status=EXPIRED", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/price-lists/{Id}/items?pageSize=101", Token)).StatusCode);
    }

    [Fact]
    public async Task Managers_reach_validation_of_the_write_endpoints()
    {
        using var client = ClientFor("STORE_OWNER");

        var create = await client.PostAsJsonAsync("/api/price-lists", new { code = "", name = "", effectiveFrom = DateTimeOffset.UtcNow }, Token);
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);

        var items = await client.PutAsJsonAsync(
            $"/api/price-lists/{Id}/items",
            new { items = new[] { new { storeProductId = Guid.NewGuid(), productPackagingId = Guid.NewGuid(), sellingPrice = 1.234m } } },
            Token);
        Assert.Equal(HttpStatusCode.BadRequest, items.StatusCode);
        var errors = JsonDocument.Parse(await items.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("errors");
        Assert.Contains(errors.EnumerateObject(), property => property.Name.Contains("sellingPrice", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Swagger_lists_the_price_list_endpoints()
    {
        using var client = ClientFor(null);

        var paths = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token)).RootElement.GetProperty("paths");

        foreach (var path in new[]
                 {
                     "/api/price-lists", "/api/price-lists/{id}", "/api/price-lists/{id}/activate", "/api/price-lists/{id}/deactivate",
                     "/api/price-lists/{id}/items", "/api/price-lists/{id}/items/{itemId}"
                 })
        {
            Assert.True(paths.TryGetProperty(path, out _), path);
        }
    }
}
