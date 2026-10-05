using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// FLOW_2 §7–9 offline checks: role gates and validation run before any database access.
public class DeliveriesHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
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

    public static TheoryData<string, string> StaffEndpoints => new()
    {
        { "POST", "/api/deliveries" }, { "GET", "/api/deliveries" }, { "GET", $"/api/deliveries/{Id}" },
        { "GET", $"/api/orders/{Id}/deliveries" }, { "POST", $"/api/deliveries/{Id}/assign" },
        { "PUT", $"/api/deliveries/{Id}/items/{Id}/lots" }, { "POST", $"/api/deliveries/{Id}/dispatch" },
        { "POST", $"/api/deliveries/{Id}/cancel" }, { "POST", $"/api/deliveries/{Id}/attempts" },
        { "GET", $"/api/deliveries/{Id}/attempts" }, { "POST", $"/api/deliveries/{Id}/attempts/{Id}/complete" },
        { "POST", $"/api/deliveries/{Id}/attempts/{Id}/cancel" }, { "POST", $"/api/deliveries/{Id}/incidents" },
        { "GET", $"/api/deliveries/{Id}/incidents" }, { "POST", $"/api/deliveries/{Id}/incidents/{Id}/resolve" },
        { "GET", "/api/reports/deliveries?fromDate=2026-10-01&toDate=2026-10-31" }
    };

    // Writes reserved for Operate (Admin, Store Owner, Sales): delivery staff get 403.
    public static TheoryData<string, string> OperateOnly => new()
    {
        { "POST", "/api/deliveries" }, { "GET", $"/api/orders/{Id}/deliveries" }, { "POST", $"/api/deliveries/{Id}/assign" },
        { "PUT", $"/api/deliveries/{Id}/items/{Id}/lots" }, { "POST", $"/api/deliveries/{Id}/dispatch" },
        { "POST", $"/api/deliveries/{Id}/cancel" }, { "POST", $"/api/deliveries/{Id}/attempts/{Id}/cancel" },
        { "POST", $"/api/deliveries/{Id}/incidents/{Id}/resolve" }
    };

    [Theory]
    [MemberData(nameof(StaffEndpoints))]
    public async Task Every_endpoint_requires_authentication(string method, string url)
    {
        using var client = Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Request(method, url), Token)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(StaffEndpoints))]
    public async Task Farmers_cannot_use_staff_delivery_routes(string method, string url)
    {
        using var client = Client("FARMER");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Request(method, url), Token)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(OperateOnly))]
    public async Task Delivery_staff_cannot_plan_dispatch_or_resolve(string method, string url)
    {
        using var client = Client("DELIVERY_STAFF");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Request(method, url), Token)).StatusCode);
    }

    [Theory]
    [InlineData("SALES_STAFF")]
    [InlineData("DELIVERY_STAFF")]
    public async Task The_delivery_report_is_manage_only(string role)
    {
        using var client = Client(role);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/reports/deliveries?fromDate=2026-10-01&toDate=2026-10-31", Token)).StatusCode);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("SALES_STAFF")]
    [InlineData("DELIVERY_STAFF")]
    public async Task Farmer_delivery_tracking_is_farmer_only(string role)
    {
        using var client = Client(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/me/orders/{Id}/deliveries", Token)).StatusCode);
    }

    public static TheoryData<string, string, object> InvalidBodies => new()
    {
        { "POST", "/api/deliveries", new { orderId = Guid.NewGuid(), items = Array.Empty<object>() } },
        { "POST", "/api/deliveries", new { orderId = Guid.NewGuid(), items = new[] { new { orderItemId = Guid.NewGuid(), plannedQuantity = 0 } } } },
        { "POST", $"/api/deliveries/{Id}/assign", new { assignedToUserId = Guid.Empty } },
        { "PUT", $"/api/deliveries/{Id}/items/{Id}/lots", new { lots = new[] { new { inventoryLotId = Guid.NewGuid(), baseQuantity = -1 } } } },
        { "POST", $"/api/deliveries/{Id}/cancel", new { reason = "" } },
        { "POST", $"/api/deliveries/{Id}/attempts/{Id}/complete", new { } },
        { "POST", $"/api/deliveries/{Id}/attempts/{Id}/complete", new { items = new[] { new { allocationId = Guid.NewGuid(), deliveredBaseQuantity = 5 } } } },
        { "POST", $"/api/deliveries/{Id}/attempts/{Id}/complete", new { failureReasonCode = "TOO_LATE" } },
        { "POST", $"/api/deliveries/{Id}/incidents", new { incidentType = "FLOOD", description = "x" } },
        { "POST", $"/api/deliveries/{Id}/incidents/{Id}/resolve", new { resolutionType = "REFUND" } }
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Invalid_body_is_400(string method, string url, object body)
    {
        using var client = Client("STORE_OWNER");
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = JsonContent.Create(body) }, Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData("/api/reports/deliveries")]
    [InlineData("/api/reports/deliveries?fromDate=2026-10-05&toDate=2026-10-01")]
    [InlineData("/api/reports/deliveries?fromDate=2025-01-01&toDate=2026-10-01")]
    [InlineData("/api/reports/deliveries?fromDate=2026-10-01&toDate=2026-10-31&groupBy=PRODUCT")]
    public async Task Invalid_report_query_is_400(string url)
    {
        using var client = Client("ADMIN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url, Token)).StatusCode);
    }

    [Fact]
    public async Task Swagger_lists_the_delivery_endpoints()
    {
        using var client = Client(null);
        var paths = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token)).RootElement.GetProperty("paths");
        foreach (var path in new[]
        {
            "/api/deliveries", "/api/deliveries/{id}", "/api/orders/{id}/deliveries", "/api/deliveries/{id}/assign",
            "/api/deliveries/{id}/items/{itemId}/lots", "/api/deliveries/{id}/dispatch", "/api/deliveries/{id}/cancel",
            "/api/deliveries/{id}/attempts", "/api/deliveries/{id}/attempts/{attemptId}/complete",
            "/api/deliveries/{id}/attempts/{attemptId}/cancel", "/api/deliveries/{id}/incidents",
            "/api/deliveries/{id}/incidents/{incidentId}/resolve", "/api/me/orders/{id}/deliveries", "/api/reports/deliveries"
        })
        {
            Assert.True(paths.TryGetProperty(path, out _), path);
        }
    }
}
