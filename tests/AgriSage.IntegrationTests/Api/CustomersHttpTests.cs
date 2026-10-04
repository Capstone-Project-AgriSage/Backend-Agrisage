using System.Net;
using System.Net.Http.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

public class CustomersHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
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
        { "GET", "/api/customers" }, { "POST", "/api/customers" }, { "GET", $"/api/customers/{Id}" },
        { "PUT", $"/api/customers/{Id}" }, { "POST", $"/api/customers/{Id}/status" }, { "PUT", $"/api/customers/{Id}/group" },
        { "GET", $"/api/customers/{Id}/group-history" }, { "GET", $"/api/customers/{Id}/orders" },
        { "GET", $"/api/customers/{Id}/debts" }, { "GET", $"/api/customers/{Id}/payments" },
        { "GET", $"/api/customers/{Id}/credit" }, { "POST", $"/api/customers/{Id}/credit" },
        { "PUT", $"/api/customers/{Id}/credit/limit" }, { "POST", $"/api/customers/{Id}/credit/activate" },
        { "POST", $"/api/customers/{Id}/credit/suspend" }, { "POST", $"/api/customers/{Id}/credit/block" },
        { "GET", $"/api/customers/{Id}/credit/history" }, { "GET", $"/api/customers/{Id}/credit/reservations" },
        { "GET", "/api/customer-groups" }, { "GET", $"/api/customer-groups/{Id}" }, { "POST", "/api/customer-groups" },
        { "PUT", $"/api/customer-groups/{Id}" }, { "POST", $"/api/customer-groups/{Id}/set-default" },
        { "POST", $"/api/customer-groups/{Id}/activate" }, { "POST", $"/api/customer-groups/{Id}/deactivate" },
        { "DELETE", $"/api/customer-groups/{Id}" }, { "PUT", $"/api/customer-groups/{Id}/price-list" },
        { "GET", $"/api/customer-groups/{Id}/price-lists" }, { "PUT", $"/api/customer-groups/{Id}/credit-tier" },
        { "GET", "/api/credit-tiers" }, { "GET", $"/api/credit-tiers/{Id}" }, { "POST", "/api/credit-tiers" },
        { "PUT", $"/api/credit-tiers/{Id}" }, { "POST", $"/api/credit-tiers/{Id}/activate" }, { "POST", $"/api/credit-tiers/{Id}/deactivate" }
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
    public async Task Farmer_and_delivery_staff_cannot_access_staff_customer_data(string method, string url)
    {
        foreach (var role in new[] { "FARMER", "DELIVERY_STAFF" })
        {
            using var client = Client(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Request(method, url), Token)).StatusCode);
        }
    }
    [Theory]
    [InlineData("/api/customers?page=0")]
    [InlineData("/api/customers?sortBy=UNKNOWN")]
    [InlineData("/api/customers?status=123")]
    [InlineData("/api/customer-groups?pageSize=101")]
    public async Task Invalid_query_is_400_before_database_access(string url)
    {
        using var client = Client("SALES_STAFF");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url, Token)).StatusCode);
    }
    [Theory]
    [InlineData("/api/customers")]
    [InlineData("/api/customer-groups")]
    [InlineData("/api/credit-tiers")]
    public async Task Invalid_creation_is_400(string url)
    {
        using var client = Client("ADMIN");
        var response = await client.PostAsJsonAsync(url, new { }, Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }
    [Fact]
    public async Task Sales_cannot_change_customer_or_credit_status()
    {
        using var client = Client("SALES_STAFF");
        foreach (var url in new[] { $"/api/customers/{Id}/status", $"/api/customers/{Id}/credit/block", $"/api/customer-groups/{Id}/set-default" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(url, new { }, Token)).StatusCode);
    }
}
