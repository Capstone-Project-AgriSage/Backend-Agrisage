using System.Net;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Reports;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgriSage.IntegrationTests.Api;

public class InventoryReportHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly string Card = $"/api/inventory/stock-card?storeProductId={Guid.NewGuid()}&fromDate=2026-10-05&toDate=2026-10-05";
    private const string Movement = "/api/reports/inventory-movement?fromDate=2026-10-05&toDate=2026-10-05";
    private const string Valuation = "/api/reports/inventory-valuation";
    private static HttpClient Client(WebApplicationFactory<Program> api, string? role)
    {
        var client = api.CreateClient();
        if (role is not null)
            client.DefaultRequestHeaders.Authorization = new("Bearer", api.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role).Value);
        return client;
    }
    [Theory]
    [InlineData(null)]
    [InlineData("FARMER")]
    [InlineData("DELIVERY_STAFF")]
    public async Task All_endpoints_require_authentication_and_operator_or_manager_role(string? role)
    {
        using var client = Client(factory, role);
        foreach (var url in new[] { Card, Movement, Valuation })
            Assert.Equal(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, (await client.GetAsync(url, Token)).StatusCode);
    }
    [Fact]
    public async Task Sales_can_read_stock_card_but_cannot_read_reports()
    {
        await using var api = StubFactory(); using var client = Client(api, "SALES_STAFF");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Card, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Movement, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Valuation, Token)).StatusCode);
    }
    [Theory]
    [InlineData("ADMIN")]
    [InlineData("STORE_OWNER")]
    public async Task Managers_receive_all_three_response_contracts(string role)
    {
        await using var api = StubFactory(); using var client = Client(api, role);
        foreach (var url in new[] { Card, Movement, Valuation })
        {
            var response = await client.GetAsync(url, Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
            Assert.True(json.RootElement.TryGetProperty(url == Card ? "closingBaseQuantity" : "totals", out _));
        }
    }
    [Theory]
    [InlineData("/api/inventory/stock-card?fromDate=2026-10-05&toDate=2026-10-05")]
    [InlineData("/api/reports/inventory-movement")]
    [InlineData("/api/reports/inventory-movement?fromDate=2026-10-05&toDate=2026-10-04")]
    [InlineData("/api/reports/inventory-movement?fromDate=2026-01-01&toDate=2027-01-02")]
    [InlineData("/api/reports/inventory-valuation?categoryId=not-guid")]
    public async Task Invalid_queries_are_400_before_the_service(string url)
    {
        using var client = Client(factory, "STORE_OWNER");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url, Token)).StatusCode);
    }
    [Fact]
    public async Task Swagger_exposes_three_get_routes()
    {
        using var client = factory.CreateClient();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token));
        foreach (var path in new[] { "/api/inventory/stock-card", "/api/reports/inventory-movement", Valuation })
            Assert.True(json.RootElement.GetProperty("paths").GetProperty(path).TryGetProperty("get", out _));
    }
    private WebApplicationFactory<Program> StubFactory() => factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
    {
        services.RemoveAll<IInventoryReportService>(); services.AddSingleton<IInventoryReportService, Stub>();
    }));
    private sealed class Stub : IInventoryReportService
    {
        public Task<StockCardResponse> GetStockCardAsync(StockCardRequest r, CancellationToken ct) =>
            Task.FromResult(new StockCardResponse(r.StoreProductId, "SKU", "Product", "KG", r.FromDate!.Value, r.ToDate!.Value, 0, [], 0));
        public Task<InventoryMovementReportResponse> GetMovementAsync(InventoryMovementReportRequest r, CancellationToken ct) =>
            Task.FromResult(new InventoryMovementReportResponse(r.FromDate!.Value, r.ToDate!.Value, [], new(0, 0, 0, 0, 0, 0, 0, 0)));
        public Task<InventoryValuationReportResponse> GetValuationAsync(InventoryValuationReportRequest r, CancellationToken ct) =>
            Task.FromResult(new InventoryValuationReportResponse([], [], new(0, 0)));
    }
}
