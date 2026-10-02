using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// Authorization and validation of suppliers, goods receipts and inventory without a database.
public class ReceivingHttpTests : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private readonly StaffHttpTests.StaffApiFactory _factory;

    public ReceivingHttpTests(StaffHttpTests.StaffApiFactory factory)
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

    // Endpoints Admin, Store Owner and Sales may call.
    public static TheoryData<string, string> OperateEndpoints => new()
    {
        { "GET", "/api/suppliers" },
        { "GET", $"/api/suppliers/{Id}" },
        { "GET", "/api/goods-receipts" },
        { "GET", $"/api/goods-receipts/{Id}" },
        { "POST", "/api/goods-receipts" },
        { "PUT", $"/api/goods-receipts/{Id}" },
        { "POST", $"/api/goods-receipts/{Id}/items" },
        { "PUT", $"/api/goods-receipts/{Id}/items/{Id}" },
        { "DELETE", $"/api/goods-receipts/{Id}/items/{Id}" },
        { "POST", $"/api/goods-receipts/{Id}/confirm" },
        { "POST", $"/api/goods-receipts/{Id}/cancel" },
        { "DELETE", $"/api/goods-receipts/{Id}" },
        { "GET", "/api/inventory/lots" },
        { "GET", $"/api/inventory/lots/{Id}" },
        { "GET", "/api/inventory/stock-movements" },
        { "GET", $"/api/inventory/stock-movements/{Id}" }
    };

    // Endpoints only Admin and Store Owner may call (confirming a receipt is open to Sales).
    public static TheoryData<string, string> ManageEndpoints => new()
    {
        { "POST", "/api/suppliers" },
        { "PUT", $"/api/suppliers/{Id}" },
        { "POST", $"/api/suppliers/{Id}/activate" },
        { "POST", $"/api/suppliers/{Id}/deactivate" },
        { "DELETE", $"/api/suppliers/{Id}" },
        { "POST", $"/api/inventory/lots/{Id}/status" }
    };

    [Theory]
    [MemberData(nameof(OperateEndpoints))]
    [MemberData(nameof(ManageEndpoints))]
    public async Task Every_endpoint_needs_a_token(string method, string url)
    {
        using var client = ClientFor(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Request(method, url), Token)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(OperateEndpoints))]
    [MemberData(nameof(ManageEndpoints))]
    public async Task Delivery_staff_and_farmers_have_no_access(string method, string url)
    {
        foreach (var role in new[] { "DELIVERY_STAFF", "FARMER" })
        {
            using var client = ClientFor(role);

            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Request(method, url), Token)).StatusCode);
        }
    }

    [Theory]
    [MemberData(nameof(ManageEndpoints))]
    public async Task Sales_staff_cannot_manage_suppliers_or_change_lot_status(string method, string url)
    {
        using var client = ClientFor("SALES_STAFF");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Request(method, url), Token)).StatusCode);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("STORE_OWNER")]
    [InlineData("SALES_STAFF")]
    public async Task Sales_can_run_receiving_and_read_stock_so_these_reach_validation(string role)
    {
        using var client = ClientFor(role);

        // pageSize=0 fails validation (400) before any database access, which shows the role was let in.
        foreach (var url in new[] { "/api/suppliers", "/api/goods-receipts", "/api/inventory/lots", "/api/inventory/stock-movements" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{url}?pageSize=0", Token)).StatusCode);
        }

        var draft = await client.PostAsJsonAsync("/api/goods-receipts", new { supplierId = Guid.Empty }, Token);
        Assert.Equal(HttpStatusCode.BadRequest, draft.StatusCode);
    }

    [Fact]
    public async Task Admin_and_store_owner_reach_the_manage_endpoints()
    {
        foreach (var role in new[] { "ADMIN", "STORE_OWNER" })
        {
            using var client = ClientFor(role);

            // Invalid bodies fail validation (400) before the database, which shows the role was let in.
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/suppliers", new { name = "" }, Token)).StatusCode);
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync($"/api/inventory/lots/{Id}/status", new { status = "DEPLETED" }, Token)).StatusCode);
        }
    }

    [Fact]
    public async Task Receipt_lines_are_validated_before_reaching_the_database()
    {
        using var client = ClientFor("SALES_STAFF");

        var response = await client.PostAsJsonAsync(
            $"/api/goods-receipts/{Id}/items",
            new { storeProductId = Guid.NewGuid(), productPackagingId = Guid.NewGuid(), receivedQuantity = 0, purchaseUnitCost = 1.234 },
            Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("receivedQuantity", out _));
        Assert.True(errors.TryGetProperty("purchaseUnitCost", out _));
    }

    [Fact]
    public async Task Swagger_lists_the_receiving_endpoints()
    {
        using var client = ClientFor(null);

        var paths = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token)).RootElement.GetProperty("paths");

        foreach (var path in new[]
        {
            "/api/suppliers", "/api/suppliers/{id}", "/api/goods-receipts", "/api/goods-receipts/{id}",
            "/api/goods-receipts/{id}/items", "/api/goods-receipts/{id}/items/{itemId}", "/api/goods-receipts/{id}/cancel",
            "/api/goods-receipts/{id}/confirm", "/api/inventory/lots", "/api/inventory/lots/{id}/status",
            "/api/inventory/stock-movements", "/api/inventory/stock-movements/{id}"
        })
        {
            Assert.True(paths.TryGetProperty(path, out _), path);
        }
    }
}
