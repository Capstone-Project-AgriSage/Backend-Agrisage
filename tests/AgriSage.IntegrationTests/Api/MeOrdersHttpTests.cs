using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// FLOW_2 §5 offline checks: Farmer-only routes and checkout validation before any database access.
public class MeOrdersHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
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
        { "POST", "/api/me/orders" }, { "GET", "/api/me/orders" }, { "GET", $"/api/me/orders/{Id}" },
        { "POST", $"/api/me/orders/{Id}/cancel" }
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
    public async Task Staff_use_the_staff_order_routes(string method, string url)
    {
        foreach (var role in new[] { "ADMIN", "STORE_OWNER", "SALES_STAFF", "DELIVERY_STAFF" })
        {
            using var client = Client(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Request(method, url), Token)).StatusCode);
        }
    }

    public static TheoryData<object> InvalidCheckouts => new()
    {
        new { },
        new { source = "COUNTER", settlementType = "FULL_PAYMENT", fulfillmentType = "PICKUP" },
        new { source = "FARMER_WEB", settlementType = "LATER", fulfillmentType = "PICKUP" },
        new { source = "FARMER_WEB", settlementType = "FULL_PAYMENT", fulfillmentType = "DELIVERY" },
        new { source = "FARMER_WEB", settlementType = "FULL_PAYMENT", fulfillmentType = "PICKUP", addressId = Guid.NewGuid() },
        new
        {
            source = "FARMER_WEB", settlementType = "FULL_PAYMENT", fulfillmentType = "DELIVERY", addressId = Guid.NewGuid(),
            deliveryAddress = new { recipientName = "A", recipientPhone = "0901234567", addressLine = "L", province = "P" }
        },
        new { source = "FARMER_WEB", settlementType = "FULL_PAYMENT", fulfillmentType = "PICKUP", note = new string('x', 1001) }
    };

    [Theory]
    [MemberData(nameof(InvalidCheckouts))]
    public async Task Invalid_checkout_is_400(object body)
    {
        using var client = Client("FARMER");
        var response = await client.PostAsJsonAsync("/api/me/orders", body, Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData("/api/me/orders?status=SHIPPED")]
    [InlineData("/api/me/orders?pageSize=101")]
    [InlineData("/api/me/orders?fromDate=2026-10-05&toDate=2026-10-01")]
    public async Task Invalid_list_query_is_400(string url)
    {
        using var client = Client("FARMER");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url, Token)).StatusCode);
    }

    [Fact]
    public async Task Swagger_lists_the_my_orders_endpoints()
    {
        using var client = Client(null);
        var paths = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token)).RootElement.GetProperty("paths");
        foreach (var path in new[] { "/api/me/orders", "/api/me/orders/{id}", "/api/me/orders/{id}/cancel" })
        {
            Assert.True(paths.TryGetProperty(path, out _), path);
        }
    }
}
