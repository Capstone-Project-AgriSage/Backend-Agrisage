using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// FLOW_2 §3.1 offline checks: role gates and validation run before any database access.
public class MeProfileHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
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
        { "GET", "/api/me/profile" }, { "PUT", "/api/me/profile" }, { "GET", "/api/me/addresses" },
        { "POST", "/api/me/addresses" }, { "PUT", $"/api/me/addresses/{Id}" }, { "DELETE", $"/api/me/addresses/{Id}" },
        { "POST", $"/api/me/addresses/{Id}/set-default" }
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
    public async Task Only_a_farmer_can_use_me_routes(string method, string url)
    {
        foreach (var role in new[] { "ADMIN", "STORE_OWNER", "SALES_STAFF", "DELIVERY_STAFF" })
        {
            using var client = Client(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Request(method, url), Token)).StatusCode);
        }
    }

    [Theory]
    [InlineData("PUT", "/api/me/profile")]
    [InlineData("POST", "/api/me/addresses")]
    public async Task Invalid_body_is_400_problem_details(string method, string url)
    {
        using var client = Client("FARMER");
        var response = await client.SendAsync(Request(method, url), Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Unknown_address_type_and_gender_are_400()
    {
        using var client = Client("FARMER");
        var address = new { recipientName = "A", recipientPhone = "0901234567", addressLine = "Line", province = "Can Tho", addressType = "OFFICE" };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/me/addresses", address, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/me/profile", new { fullName = "A", gender = "X" }, Token)).StatusCode);
    }

    [Theory]
    [InlineData("FARMER")]
    [InlineData("DELIVERY_STAFF")]
    public async Task Staff_customer_addresses_are_operate_only(string role)
    {
        using var client = Client(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/customers/{Id}/addresses", Token)).StatusCode);
    }

    [Fact]
    public async Task Swagger_lists_the_profile_and_address_endpoints()
    {
        using var client = Client(null);
        var paths = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token)).RootElement.GetProperty("paths");
        foreach (var path in new[]
        {
            "/api/me/profile", "/api/me/addresses", "/api/me/addresses/{id}", "/api/me/addresses/{id}/set-default",
            "/api/customers/{farmerProfileId}/addresses"
        })
        {
            Assert.True(paths.TryGetProperty(path, out _), path);
        }
    }
}
