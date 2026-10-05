using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Inventory;
using AgriSage.Application.Features.Stocktakes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgriSage.IntegrationTests.Api;

public class StocktakeAdjustmentHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static readonly Guid Id = Guid.NewGuid();
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public static TheoryData<string, string> Endpoints => new()
    {
        { "POST", "/api/stocktakes" }, { "GET", "/api/stocktakes" }, { "GET", $"/api/stocktakes/{Id}" },
        { "POST", $"/api/stocktakes/{Id}/start" }, { "PUT", $"/api/stocktakes/{Id}/counts" },
        { "POST", $"/api/stocktakes/{Id}/refresh-stale" }, { "POST", $"/api/stocktakes/{Id}/complete" },
        { "POST", $"/api/stocktakes/{Id}/cancel" }, { "DELETE", $"/api/stocktakes/{Id}" },
        { "POST", "/api/inventory/adjustments" }
    };

    private static HttpClient ClientFor(WebApplicationFactory<Program> app, string? role)
    {
        var client = app.CreateClient();
        if (role is not null)
        {
            var jwt = app.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role);
            client.DefaultRequestHeaders.Authorization = new("Bearer", jwt.Value);
        }
        return client;
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task All_endpoints_require_a_staff_token(string method, string url)
    {
        foreach (var role in new string?[] { null, "FARMER", "DELIVERY_STAFF" })
        {
            using var client = ClientFor(factory, role);
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            Assert.Equal(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden,
                (await client.SendAsync(request, Token)).StatusCode);
        }
    }

    [Theory]
    [InlineData("/api/inventory/adjustments")]
    [InlineData("/api/stocktakes/{id}/complete")]
    public async Task Sales_cannot_approve_stocktakes_or_post_manual_adjustments(string route)
    {
        using var client = ClientFor(factory, "SALES_STAFF");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(route.Replace("{id}", Id.ToString()), null, Token)).StatusCode);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("STORE_OWNER")]
    [InlineData("SALES_STAFF")]
    public async Task Operators_reach_stocktake_validation(string role)
    {
        using var client = ClientFor(factory, role);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/stocktakes?pageSize=0", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/stocktakes?status=BAD", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/stocktakes?fromDate=0001-01-01", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/stocktakes?toDate=0001-01-01", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/stocktakes", new { storeProductIds = new[] { Guid.Empty } }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/stocktakes/{Id}/counts", new { counts = new[] { new { itemId = Id, countedQuantity = -1 } } }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/stocktakes/{Id}/counts", new { counts = Array.Empty<object>() }, Token)).StatusCode);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("STORE_OWNER")]
    public async Task Mixed_adjustment_signs_are_rejected_before_database_access(string role)
    {
        using var client = ClientFor(factory, role);
        var response = await client.PostAsJsonAsync("/api/inventory/adjustments", new StockAdjustmentRequest("OTHER", "Test",
            [new(Guid.NewGuid(), 1), new(Guid.NewGuid(), -1)]), Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("lines", out _));
    }

    [Fact]
    public async Task Authorized_routes_return_contract_status_codes_and_created_locations()
    {
        var stocktakes = new StocktakeStub();
        var adjustments = new AdjustmentStub();
        await using var app = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IStocktakeService>();
            services.AddSingleton<IStocktakeService>(stocktakes);
            services.RemoveAll<IStockAdjustmentService>();
            services.AddSingleton<IStockAdjustmentService>(adjustments);
        }));
        using var client = ClientFor(app, "STORE_OWNER");
        var created = await client.PostAsJsonAsync("/api/stocktakes", new CreateStocktakeRequest(), Token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.EndsWith($"/api/stocktakes/{Id}", created.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/stocktakes", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/stocktakes/{Id}?onlyDifferences=true", Token)).StatusCode);
        foreach (var action in new[] { "start", "refresh-stale", "complete" })
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/stocktakes/{Id}/{action}", null, Token)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/stocktakes/{Id}/counts", new StocktakeCountsRequest([new(Id, 7)]), Token)).StatusCode);
        Assert.Equal(7, stocktakes.LastCounts!.Counts[0].CountedQuantity);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/stocktakes/{Id}/cancel", new CancelStocktakeRequest("Test"), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/stocktakes/{Id}", Token)).StatusCode);
        var adjusted = await client.PostAsJsonAsync("/api/inventory/adjustments", new StockAdjustmentRequest("OTHER", "Test", [new(Id, -1)]), Token);
        Assert.Equal(HttpStatusCode.Created, adjusted.StatusCode);
        Assert.EndsWith($"/api/inventory/stock-movements/{Id}", adjusted.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Swagger_lists_every_stocktake_action_and_manual_adjustment()
    {
        using var client = ClientFor(factory, null);
        using var json = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token));
        var paths = json.RootElement.GetProperty("paths");
        foreach (var row in Endpoints)
        {
            var url = row.Data.Item2;
            Assert.True(paths.TryGetProperty(url.Replace(Id.ToString(), "{id}"), out _), url);
        }
    }

    private sealed class StocktakeStub : IStocktakeService
    {
        public StocktakeCountsRequest? LastCounts { get; private set; }
        private static Task<StocktakeResponse> Response() => Task.FromResult(new StocktakeResponse(Id, "ST-test", "DRAFT",
            null, Id, DateTimeOffset.UtcNow, null, null, null, null, new(0, 0, 0, 0), [], []));
        public Task<StocktakeResponse> CreateAsync(CreateStocktakeRequest request, CancellationToken token) => Response();
        public Task<PagedResult<StocktakeListItem>> ListAsync(StocktakeListRequest request, CancellationToken token) => Task.FromResult(new PagedResult<StocktakeListItem>([], 1, 20, 0));
        public Task<StocktakeResponse> GetAsync(Guid id, StocktakeDetailRequest request, CancellationToken token) => Response();
        public Task<StocktakeResponse> StartAsync(Guid id, CancellationToken token) => Response();
        public Task<StocktakeResponse> CountAsync(Guid id, StocktakeCountsRequest request, CancellationToken token) { LastCounts = request; return Response(); }
        public Task<StocktakeResponse> RefreshStaleAsync(Guid id, CancellationToken token) => Response();
        public Task<StocktakeResponse> CompleteAsync(Guid id, CancellationToken token) => Response();
        public Task<StocktakeResponse> CancelAsync(Guid id, CancelStocktakeRequest request, CancellationToken token) => Response();
        public Task DeleteAsync(Guid id, CancellationToken token) => Task.CompletedTask;
    }

    private sealed class AdjustmentStub : IStockAdjustmentService
    {
        public Task<StockMovementResponse> CreateAsync(StockAdjustmentRequest request, CancellationToken token) =>
            Task.FromResult(new StockMovementResponse(Id, "SM-test", "ADJUSTMENT_OUT", "POSTED", DateTimeOffset.UtcNow,
                null, null, null, null, null, "OTHER", "Test", Id, DateTimeOffset.UtcNow, Id, []));
    }
}
