using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// Order confirmation, pickup and cancel-remaining endpoints (F1.4, F1.5) without a database: roles, validation and Swagger.
public class OrderConfirmationHttpTests : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private readonly StaffHttpTests.StaffApiFactory _factory;

    public OrderConfirmationHttpTests(StaffHttpTests.StaffApiFactory factory)
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
        new(new HttpMethod(method), url) { Content = method == "GET" ? null : JsonContent.Create(new { }) };

    public static TheoryData<string, string> Endpoints => new()
    {
        { "GET", $"/api/orders/{Id}/fefo-suggestions" },
        { "POST", $"/api/orders/{Id}/confirm" },
        { "POST", $"/api/orders/{Id}/start-preparing" },
        { "POST", $"/api/orders/{Id}/mark-ready" },
        { "GET", $"/api/orders/{Id}/reservation" },
        { "POST", $"/api/orders/{Id}/pickup" },
        { "POST", $"/api/orders/{Id}/items/{Id}/cancel-remaining" }
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

    [Fact]
    public async Task A_route_id_that_is_not_a_guid_is_not_found()
    {
        using var client = ClientFor("SALES_STAFF");

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/orders/not-a-guid/confirm", null, Token)).StatusCode);
    }

    [Fact]
    public async Task Staff_reach_validation_of_pickup_and_cancel_remaining()
    {
        using var client = ClientFor("SALES_STAFF");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/orders/{Id}/pickup", new { items = Array.Empty<object>() }, Token)).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/api/orders/{Id}/pickup", new { items = new[] { new { orderItemId = Guid.NewGuid(), lots = new[] { new { inventoryLotId = Guid.NewGuid(), baseQuantity = 0 } } } } }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/orders/{Id}/items/{Id}/cancel-remaining", new { reason = "" }, Token)).StatusCode);
    }

    [Fact]
    public async Task Swagger_lists_the_confirmation_endpoints()
    {
        using var client = ClientFor(null);

        var paths = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token)).RootElement.GetProperty("paths");

        foreach (var path in new[]
                 {
                     "/api/orders/{id}/fefo-suggestions", "/api/orders/{id}/confirm", "/api/orders/{id}/start-preparing",
                     "/api/orders/{id}/mark-ready", "/api/orders/{id}/reservation", "/api/orders/{id}/pickup",
                     "/api/orders/{id}/items/{itemId}/cancel-remaining"
                 })
        {
            Assert.True(paths.TryGetProperty(path, out _), path);
        }
    }
}
