using System.Net;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// Report endpoints (F1.8) without a database: roles, validation and Swagger.
public class ReportsHttpTests : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private readonly StaffHttpTests.StaffApiFactory _factory;

    public ReportsHttpTests(StaffHttpTests.StaffApiFactory factory)
    {
        _factory = factory;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

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

    private const string Query = "?fromDate=2026-10-01&toDate=2026-10-31";

    [Fact]
    public async Task The_sales_report_needs_a_token_and_is_closed_to_everyone_but_admin_and_store_owner()
    {
        using (var anonymous = ClientFor(null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/reports/sales" + Query, Token)).StatusCode);
        }

        foreach (var role in new[] { "SALES_STAFF", "DELIVERY_STAFF", "FARMER" })
        {
            using var client = ClientFor(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/reports/sales" + Query, Token)).StatusCode);
        }
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("STORE_OWNER")]
    public async Task Managers_reach_validation(string role)
    {
        using var client = ClientFor(role);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/reports/sales", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/reports/sales?fromDate=2026-10-31&toDate=2026-10-01", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/reports/sales?fromDate=2025-01-01&toDate=2026-10-01", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/reports/sales" + Query + "&groupBy=MONTH", Token)).StatusCode);
    }

    [Fact]
    public async Task Swagger_lists_the_sales_report()
    {
        using var client = ClientFor(null);

        var paths = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token)).RootElement.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/api/reports/sales", out _));
    }
}
