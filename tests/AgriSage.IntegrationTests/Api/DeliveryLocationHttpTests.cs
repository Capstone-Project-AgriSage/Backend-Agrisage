using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// FLOW_2 §9a (F2.8) offline checks: role gates and validation run before any database access.
public class DeliveryLocationHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly string Id = Guid.NewGuid().ToString();
    private static readonly string Locations = $"/api/deliveries/{Id}/attempts/{Id}/locations";
    private static readonly string Tracking = $"/api/me/orders/{Id}/deliveries/{Id}/tracking";

    private HttpClient Client(string? role)
    {
        var client = factory.CreateClient();
        if (role != null)
            client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role).Value);
        return client;
    }

    private static object Point(double latitude = 10.04, double longitude = 105.74, int second = 0) =>
        new { latitude, longitude, accuracyMeters = 12.5, recordedAt = new DateTimeOffset(2026, 11, 27, 3, 10, second % 60, TimeSpan.Zero).AddMinutes(second / 60) };

    private static HttpRequestMessage Request(string method, string url, object? body = null) => new(new HttpMethod(method), url)
    { Content = method == "POST" ? JsonContent.Create(body ?? new { points = new[] { Point() } }) : null };

    public static TheoryData<string, string> StaffEndpoints => new()
    {
        { "POST", Locations }, { "GET", Locations }, { "GET", "/api/deliveries/live" }
    };

    [Theory]
    [MemberData(nameof(StaffEndpoints))]
    [InlineData("GET", "/api/me/orders/00000000-0000-0000-0000-000000000001/deliveries/00000000-0000-0000-0000-000000000001/tracking")]
    public async Task Every_endpoint_requires_authentication(string method, string url)
    {
        using var client = Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Request(method, url), Token)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(StaffEndpoints))]
    public async Task Farmers_cannot_use_the_staff_location_routes(string method, string url)
    {
        using var client = Client("FARMER");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Request(method, url), Token)).StatusCode);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("STORE_OWNER")]
    [InlineData("SALES_STAFF")]
    [InlineData("DELIVERY_STAFF")]
    public async Task Order_tracking_is_farmer_only(string role)
    {
        using var client = Client(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Tracking, Token)).StatusCode);
    }

    public static TheoryData<string, object> InvalidBodies => new()
    {
        { "DELIVERY_STAFF", new { points = Array.Empty<object>() } },
        { "DELIVERY_STAFF", new { } },
        { "DELIVERY_STAFF", new { points = new[] { Point(latitude: 91) } } },
        { "DELIVERY_STAFF", new { points = new[] { Point(longitude: -181) } } },
        { "DELIVERY_STAFF", new { points = new[] { new { latitude = 10.0, longitude = 105.0, accuracyMeters = -1.0, recordedAt = DateTimeOffset.UtcNow } } } },
        { "DELIVERY_STAFF", new { points = new[] { new { latitude = 10.0, longitude = 105.0 } } } },
        { "STORE_OWNER", new { points = Enumerable.Range(0, 101).Select(i => Point(second: i)).ToArray() } }
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Invalid_batch_is_400(string role, object body)
    {
        using var client = Client(role);
        var response = await client.SendAsync(Request("POST", Locations, body), Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Swagger_lists_the_location_endpoints()
    {
        using var client = Client(null);
        var paths = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token)).RootElement.GetProperty("paths");
        foreach (var path in new[]
        {
            "/api/deliveries/{id}/attempts/{attemptId}/locations", "/api/deliveries/live",
            "/api/me/orders/{id}/deliveries/{deliveryId}/tracking"
        })
        {
            Assert.True(paths.TryGetProperty(path, out _), path);
        }
    }
}
