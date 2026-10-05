using System.Net;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Inventory;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgriSage.IntegrationTests.Api;

public class InventoryOverviewHttpTests : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private readonly StaffHttpTests.StaffApiFactory _factory;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public InventoryOverviewHttpTests(StaffHttpTests.StaffApiFactory factory) => _factory = factory;

    private static HttpClient ClientFor(WebApplicationFactory<Program> factory, string? role)
    {
        var client = factory.CreateClient();
        if (role is not null)
        {
            var token = factory.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role);
            client.DefaultRequestHeaders.Authorization = new("Bearer", token.Value);
        }

        return client;
    }

    public static TheoryData<string, string> Endpoints => new()
    {
        { "GET", "/api/inventory/stock-summary" },
        { "GET", "/api/inventory/alerts" },
        { "POST", "/api/inventory/lots/expire-due" }
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoints_require_authentication_and_deny_farmer_and_delivery_staff(string method, string url)
    {
        foreach (var role in new string?[] { null, "FARMER", "DELIVERY_STAFF" })
        {
            using var client = ClientFor(_factory, role);
            using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url), Token);
            Assert.Equal(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Sales_cannot_expire_lots()
    {
        using var client = ClientFor(_factory, "SALES_STAFF");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/inventory/lots/expire-due", null, Token)).StatusCode);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("STORE_OWNER")]
    [InlineData("SALES_STAFF")]
    public async Task Operators_reach_query_validation(string role)
    {
        using var client = ClientFor(_factory, role);
        foreach (var url in new[]
        {
            "/api/inventory/stock-summary?page=0", "/api/inventory/stock-summary?pageSize=101",
            "/api/inventory/stock-summary?categoryId=bad", "/api/inventory/stock-summary?hasStock=bad",
            "/api/inventory/alerts?type=ACTIVE", "/api/inventory/alerts?withinDays=0",
            "/api/inventory/alerts?withinDays=366", "/api/inventory/alerts?pageSize=0"
        })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url, Token)).StatusCode);
        }
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("STORE_OWNER")]
    public async Task Managers_reach_the_expiration_use_case_and_receive_its_result(string role)
    {
        var service = new ExpirationStub();
        await using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IInventoryService>();
            services.AddSingleton<IInventoryService>(service);
        }));
        using var client = ClientFor(factory, role);

        var response = await client.PostAsync("/api/inventory/lots/expire-due", null, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(service.Called);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal(1, json.RootElement.GetProperty("expiredLotCount").GetInt32());
        Assert.Equal("2026-10-04", json.RootElement.GetProperty("lots")[0].GetProperty("expiryDate").GetString());
    }

    [Fact]
    public async Task Swagger_exposes_all_three_routes()
    {
        using var client = ClientFor(_factory, null);
        using var json = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token));
        var paths = json.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/inventory/stock-summary").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/inventory/alerts").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/inventory/lots/expire-due").TryGetProperty("post", out _));
    }

    private sealed class ExpirationStub : IInventoryService
    {
        public bool Called { get; private set; }

        public Task<ExpireDueLotsResponse> ExpireDueLotsAsync(CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(new ExpireDueLotsResponse(1, [new(Guid.NewGuid(), "L1", new DateOnly(2026, 10, 4))]));
        }

        public Task<PagedResult<StockSummaryItem>> GetStockSummaryAsync(StockSummaryRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PagedResult<InventoryAlertItem>> GetAlertsAsync(InventoryAlertsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PagedResult<InventoryLotResponse>> ListLotsAsync(InventoryLotListRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InventoryLotResponse> GetLotAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InventoryLotResponse> ChangeLotStatusAsync(Guid id, ChangeLotStatusRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PagedResult<StockMovementListItem>> ListMovementsAsync(StockMovementListRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StockMovementResponse> GetMovementAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
