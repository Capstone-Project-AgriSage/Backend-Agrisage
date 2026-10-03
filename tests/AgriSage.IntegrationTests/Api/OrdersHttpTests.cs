using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// Counter order endpoints (F1.2) without a database: roles, validation and Swagger.
public class OrdersHttpTests : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private readonly StaffHttpTests.StaffApiFactory _factory;

    public OrdersHttpTests(StaffHttpTests.StaffApiFactory factory)
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

    public static TheoryData<string, string> Endpoints => new()
    {
        { "POST", "/api/orders" },
        { "GET", "/api/orders" },
        { "GET", $"/api/orders/{Id}" },
        { "PUT", $"/api/orders/{Id}" },
        { "POST", $"/api/orders/{Id}/items" },
        { "PUT", $"/api/orders/{Id}/items/{Id}" },
        { "PUT", $"/api/orders/{Id}/items/{Id}/price" },
        { "DELETE", $"/api/orders/{Id}/items/{Id}/price" },
        { "DELETE", $"/api/orders/{Id}/items/{Id}" }
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
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
    [InlineData("ADMIN")]
    [InlineData("STORE_OWNER")]
    [InlineData("SALES_STAFF")]
    public async Task Staff_reach_validation_of_the_list_and_create_endpoints(string role)
    {
        using var client = ClientFor(role);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/orders?pageSize=0", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/orders?status=SHIPPED", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/orders?fromDate=2026-10-03&toDate=2026-10-01", Token)).StatusCode);

        var create = await client.PostAsJsonAsync(
            "/api/orders",
            new
            {
                customerType = "WALK_IN",
                settlementType = "FULL_PAYMENT",
                fulfillmentType = "DELIVERY",
                items = new[] { new { storeProductId = Guid.NewGuid(), productPackagingId = Guid.NewGuid(), quantity = 0 } }
            },
            Token);
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        var errors = JsonDocument.Parse(await create.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("errors");
        Assert.Contains(errors.EnumerateObject(), property => property.Name.Contains("quantity", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors.EnumerateObject(), property => property.Name.Contains("Address", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Staff_reach_validation_of_the_item_endpoints()
    {
        using var client = ClientFor("SALES_STAFF");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/orders/{Id}/items", new { quantity = 1 }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/orders/{Id}/items/{Id}", new { quantity = 0 }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/orders/{Id}/items/{Id}/price", new { unitPrice = 10, reason = "" }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/orders/{Id}/items/{Id}/price", new { unitPrice = 10.001, reason = "x" }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/orders/{Id}", new { addressId = Guid.NewGuid(), deliveryAddress = new { recipientName = "A", recipientPhone = "0912345678", addressLine = "B", province = "C" } }, Token)).StatusCode);
    }

    [Fact]
    public async Task Swagger_lists_the_order_endpoints()
    {
        using var client = ClientFor(null);

        var paths = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token)).RootElement.GetProperty("paths");

        foreach (var path in new[]
                 {
                     "/api/orders", "/api/orders/{id}", "/api/orders/{id}/items", "/api/orders/{id}/items/{itemId}",
                     "/api/orders/{id}/items/{itemId}/price"
                 })
        {
            Assert.True(paths.TryGetProperty(path, out _), path);
        }
    }
}
