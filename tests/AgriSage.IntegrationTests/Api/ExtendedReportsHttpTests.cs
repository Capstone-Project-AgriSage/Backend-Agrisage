using System.Net;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

public class ExtendedReportsHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly string[] Routes = ["revenue", "revenue-summary", "orders", "payments", "purchases", "returns", "refunds", "credit-exposure"];
    private const string Period = "?fromDate=2026-10-01&toDate=2026-10-31";
    private HttpClient Client(string? role)
    {
        var client = factory.CreateClient();
        if (role != null) client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role).Value);
        return client;
    }
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("FARMER", HttpStatusCode.Forbidden)]
    [InlineData("SALES_STAFF", HttpStatusCode.Forbidden)]
    [InlineData("DELIVERY_STAFF", HttpStatusCode.Forbidden)]
    public async Task Every_new_report_is_manager_only(string? role, HttpStatusCode expected)
    {
        using var client = Client(role);
        foreach (var route in Routes) Assert.Equal(expected, (await client.GetAsync("/api/reports/" + route + Period, Token)).StatusCode);
    }
    [Theory]
    [InlineData("ADMIN")]
    [InlineData("STORE_OWNER")]
    public async Task Invalid_periods_and_groupings_fail_before_database_access(string role)
    {
        using var client = Client(role);
        foreach (var route in Routes.Where(r => r != "credit-exposure"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/reports/" + route, Token)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/reports/" + route + "?fromDate=2026-10-31&toDate=2026-10-01", Token)).StatusCode);
        }
        foreach (var route in Routes.Where(r => r is not ("credit-exposure" or "revenue-summary")))
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/reports/" + route + Period + "&groupBy=BOGUS", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/reports/revenue" + Period + "&pageSize=101", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/reports/revenue-summary?fromDate=0001-01-01&toDate=0001-01-01", Token)).StatusCode);
    }
    [Fact]
    public async Task Swagger_exposes_all_new_routes_and_revenue_fields()
    {
        using var client = Client(null);
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token));
        var paths = doc.RootElement.GetProperty("paths");
        foreach (var route in Routes) Assert.True(paths.TryGetProperty("/api/reports/" + route, out _));
        var parameters = paths.GetProperty("/api/reports/revenue").GetProperty("get").GetProperty("parameters").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "fromDate", "toDate", "groupBy", "staffUserId", "farmerProfileId", "categoryId", "source", "settlementType", "pageSize" })
            Assert.Contains(name, parameters);
    }
}
