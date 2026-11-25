using System.Net;
using System.Net.Http.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

public class ExchangesHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private HttpClient Client(string? role)
    {
        var client = factory.CreateClient();
        if (role != null) client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role).Value);
        return client;
    }
    [Theory]
    [InlineData(null)] [InlineData("FARMER")] [InlineData("DELIVERY_STAFF")]
    public async Task Staff_exchange_routes_enforce_roles(string? role)
    {
        using var c = Client(role); var expected = role == null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        Assert.Equal(expected, (await c.GetAsync("/api/exchanges", Token)).StatusCode);
        Assert.Equal(expected, (await c.PostAsJsonAsync("/api/exchanges", new { }, Token)).StatusCode);
    }
    [Theory]
    [InlineData("ADMIN")] [InlineData("SALES_STAFF")] [InlineData("STORE_OWNER")]
    public async Task Farmer_routes_exclude_staff_and_invalid_requests_are_validated(string role)
    {
        using var c = Client(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/me/exchanges", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/exchanges", new { orderId = Guid.NewGuid(), requestKey = Guid.NewGuid(), reason = "Test", items = Array.Empty<object>(), replacements = Array.Empty<object>() }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/exchanges?page=0&status=INVALID", Token)).StatusCode);
    }
    [Fact]
    public async Task Farmer_request_validation_runs_before_business_access()
    {
        using var c = Client("FARMER");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/me/exchanges", new { orderId = Guid.NewGuid(), requestKey = Guid.NewGuid(), reason = "", items = Array.Empty<object>(), replacements = Array.Empty<object>() }, Token)).StatusCode);
    }
}
