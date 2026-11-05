using System.Net;
using System.Net.Http.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// F3.11 routes: authentication, roles and request validation, before any database access.
public sealed class CropSeasonHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
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

    public static TheoryData<string, string> StoreRoutes => new()
    {
        { "GET", "/api/crop-seasons" }, { "GET", $"/api/crop-seasons/{Id}" },
        { "POST", "/api/crop-seasons" }, { "PUT", $"/api/crop-seasons/{Id}" },
    };

    private static HttpRequestMessage Request(string method, string url) => new(new HttpMethod(method), url)
    { Content = method is "POST" or "PUT" ? JsonContent.Create(new { }) : null };

    [Theory]
    [MemberData(nameof(StoreRoutes))]
    public async Task Store_routes_need_staff_and_refuse_farmer_and_delivery(string method, string url)
    {
        using var anonymous = Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Request(method, url), Token)).StatusCode);
        foreach (var role in new[] { "FARMER", "DELIVERY_STAFF" })
        {
            using var c = Client(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Request(method, url), Token)).StatusCode);
        }
    }

    // Season dates decide every credit customer's due date: Sale staff never create or change them.
    [Theory]
    [InlineData("POST", "/api/crop-seasons")]
    [InlineData("PUT", "/api/crop-seasons/{id}")]
    public async Task Sale_staff_cannot_write_seasons(string method, string route)
    {
        using var c = Client("SALES_STAFF");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Request(method, route.Replace("{id}", Id)), Token)).StatusCode);
    }

    [Fact]
    public async Task The_farmer_route_is_only_for_farmers()
    {
        using var anonymous = Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/me/crop-seasons/current", Token)).StatusCode);
        foreach (var role in new[] { "SALES_STAFF", "STORE_OWNER", "DELIVERY_STAFF" })
        {
            using var c = Client(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/me/crop-seasons/current", Token)).StatusCode);
        }
    }

    [Fact]
    public async Task Invalid_requests_are_rejected_before_database_access()
    {
        using var c = Client("ADMIN");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/crop-seasons",
            new { code = "", name = "X", harvestStart = "2027-02-15", harvestEnd = "2027-03-15" }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/crop-seasons",
            new { code = "DX", name = "X", harvestStart = "2027-02-15", harvestEnd = "2027-02-15" }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/crop-seasons",
            new { code = "DX", name = "X", harvestStart = "2027-02-15", harvestEnd = "2027-03-15", graceDays = 400 }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/api/crop-seasons/{Id}",
            new { name = "X", harvestStart = "2027-02-15", harvestEnd = "2027-03-15", graceDays = 30, version = 0, applyNewDueDateToCollectingDebts = true }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/crop-seasons?status=BOGUS", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/crop-seasons?pageSize=0", Token)).StatusCode);
    }
}
