using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// Payment endpoints (F1.3) without a database: roles, validation and Swagger.
public class PaymentsHttpTests : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private readonly StaffHttpTests.StaffApiFactory _factory;

    public PaymentsHttpTests(StaffHttpTests.StaffApiFactory factory)
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

    public static TheoryData<string, string> StaffEndpoints => new()
    {
        { "POST", "/api/payments/cash" },
        { "GET", "/api/payments" },
        { "GET", $"/api/payments/{Id}" },
        { "GET", $"/api/orders/{Id}/payments" },
        { "POST", $"/api/payments/{Id}/cancel" }
    };

    public static TheoryData<string, string> FarmerEndpoints => new()
    {
        { "GET", "/api/me/payments" },
        { "GET", $"/api/me/payments/{Id}" },
        { "GET", $"/api/me/orders/{Id}/payments" }
    };

    [Theory]
    [MemberData(nameof(StaffEndpoints))]
    public async Task Staff_endpoints_need_a_token_and_are_closed_to_delivery_staff_and_farmers(string method, string url)
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
    [MemberData(nameof(FarmerEndpoints))]
    public async Task Farmer_endpoints_need_a_token_and_are_closed_to_every_staff_role(string method, string url)
    {
        using (var anonymous = ClientFor(null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Request(method, url), Token)).StatusCode);
        }

        foreach (var role in new[] { "ADMIN", "STORE_OWNER", "SALES_STAFF", "DELIVERY_STAFF" })
        {
            using var client = ClientFor(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Request(method, url), Token)).StatusCode);
        }
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("STORE_OWNER")]
    [InlineData("SALES_STAFF")]
    public async Task Staff_reach_validation_of_the_list_and_cash_endpoints(string role)
    {
        using var client = ClientFor(role);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/payments?pageSize=0", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/payments?paymentMethod=CARD", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/payments?fromDate=2026-10-03&toDate=2026-10-01", Token)).StatusCode);

        var cash = await client.PostAsJsonAsync("/api/payments/cash", new { paymentContext = "ORDER_PAYMENT", amount = 10.005 }, Token);
        Assert.Equal(HttpStatusCode.BadRequest, cash.StatusCode);
        var errors = JsonDocument.Parse(await cash.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("errors");
        Assert.Contains(errors.EnumerateObject(), property => property.Name.Contains("amount", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors.EnumerateObject(), property => property.Name.Contains("orderId", StringComparison.OrdinalIgnoreCase));

        var repayment = await client.PostAsJsonAsync("/api/payments/cash", new { paymentContext = "DEBT_REPAYMENT", amount = 100 }, Token);
        Assert.Equal(HttpStatusCode.BadRequest, repayment.StatusCode);
    }

    [Fact]
    public async Task A_farmer_reaches_validation_of_the_own_payment_list()
    {
        using var client = ClientFor("FARMER");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/me/payments?status=LOST", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/me/payments?pageSize=101", Token)).StatusCode);
    }

    [Fact]
    public async Task Swagger_lists_the_payment_endpoints()
    {
        using var client = ClientFor(null);

        var paths = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token)).RootElement.GetProperty("paths");

        foreach (var path in new[]
                 {
                     "/api/payments/cash", "/api/payments", "/api/payments/{id}", "/api/payments/{id}/cancel", "/api/orders/{id}/payments",
                     "/api/me/payments", "/api/me/payments/{id}", "/api/me/orders/{id}/payments"
                 })
        {
            Assert.True(paths.TryGetProperty(path, out _), path);
        }
    }
}
