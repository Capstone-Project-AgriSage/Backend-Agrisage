using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// FLOW_2 §4 offline checks: Farmer-only routes and validation before any database access.
public class MeCartHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly string Id = Guid.NewGuid().ToString();

    private HttpClient Client(string? role)
    {
        var client = factory.CreateClient();
        if (role != null)
            client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role).Value);
        return client;
    }

    private static HttpRequestMessage Request(string method, string url) => new(new HttpMethod(method), url)
    { Content = method is "POST" or "PUT" ? JsonContent.Create(new { }) : null };

    public static TheoryData<string, string> Endpoints => new()
    {
        { "GET", "/api/me/cart" }, { "DELETE", "/api/me/cart" }, { "POST", "/api/me/cart/items" },
        { "PUT", $"/api/me/cart/items/{Id}" }, { "DELETE", $"/api/me/cart/items/{Id}" }
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Every_endpoint_requires_authentication(string method, string url)
    {
        using var client = Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Request(method, url), Token)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Staff_have_no_cart(string method, string url)
    {
        foreach (var role in new[] { "ADMIN", "STORE_OWNER", "SALES_STAFF", "DELIVERY_STAFF" })
        {
            using var client = Client(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Request(method, url), Token)).StatusCode);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_000_001)]
    public async Task Quantity_must_be_a_positive_packaging_count(long quantity)
    {
        using var client = Client("FARMER");
        var add = await client.PostAsJsonAsync("/api/me/cart/items",
            new { storeProductId = Guid.NewGuid(), productPackagingId = Guid.NewGuid(), quantity }, Token);
        Assert.Equal(HttpStatusCode.BadRequest, add.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/me/cart/items/{Id}", new { quantity }, Token)).StatusCode);
    }

    [Fact]
    public async Task Missing_ids_are_400()
    {
        using var client = Client("FARMER");
        var response = await client.SendAsync(Request("POST", "/api/me/cart/items"), Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Swagger_lists_the_cart_endpoints()
    {
        using var client = Client(null);
        var paths = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token)).RootElement.GetProperty("paths");
        foreach (var path in new[] { "/api/me/cart", "/api/me/cart/items", "/api/me/cart/items/{itemId}" })
        {
            Assert.True(paths.TryGetProperty(path, out _), path);
        }
    }
}
