using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Inventory;
using AgriSage.Application.Features.Permissions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgriSage.IntegrationTests.Api;

public class ReplenishmentHttpTests : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private readonly StaffHttpTests.StaffApiFactory factory;
    public ReplenishmentHttpTests(StaffHttpTests.StaffApiFactory factory) => this.factory = factory;
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private HttpClient Client(WebApplicationFactory<Program> app, string? role)
    {
        var client = app.CreateClient();
        if (role is not null) client.DefaultRequestHeaders.Authorization = new("Bearer",
            app.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role).Value);
        return client;
    }
    [Theory]
    [InlineData("/api/inventory/replenishment/suggestions")]
    [InlineData("/api/inventory/replenishment/stockout")]
    [InlineData("/api/inventory/replenishment/forecast")]
    [InlineData("/api/inventory/replenishment/seasonal")]
    [InlineData("/api/inventory/seasonal-rules")]
    [InlineData("/api/inventory/expiry-risk")]
    [InlineData("/api/reports/inventory-turnover")]
    [InlineData("/api/reports/inventory-movement/export")]
    public async Task New_reads_are_private_and_sales_need_a_delegated_permission(string route)
    {
        foreach (var role in new string?[] { null, "FARMER", "DELIVERY_STAFF", "SALES_STAFF" }) {
            using var client = Client(factory, role);
            Assert.Equal(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden,
                (await client.GetAsync(route, Token)).StatusCode);
        }
    }
    [Theory]
    [InlineData("historyDays=13")]
    [InlineData("horizonDays=91")]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("sortBy=sql")]
    [InlineData("priority=INVALID")]
    public async Task Bad_planning_queries_are_rejected_before_database_access(string query)
    {
        using var client = Client(factory, "STORE_OWNER");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/inventory/replenishment/suggestions?" + query, Token)).StatusCode);
    }
    [Fact]
    public async Task Planning_binds_queries_and_returns_global_summary_separately_from_page()
    {
        var stub = new PlanningStub();
        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(s => {
            s.RemoveAll<IReplenishmentService>(); s.AddSingleton<IReplenishmentService>(stub);
        }));
        using var client = Client(app, "STORE_OWNER");
        var response = await client.GetAsync("/api/inventory/replenishment/forecast?search=ABC&page=2&pageSize=10&historyDays=30&priority=high", Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("forecast", stub.View); Assert.Equal("ABC", stub.Request!.Search); Assert.Equal(2, stub.Request.Page);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement;
        Assert.Equal(21, json.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, json.GetProperty("totalPages").GetInt32());
        Assert.Equal(21, json.GetProperty("summary").GetProperty("totalProducts").GetInt32());
    }
    [Fact]
    public async Task Rule_writes_validate_products_dates_and_duplicates()
    {
        using var client = Client(factory, "STORE_OWNER");
        var id = Guid.NewGuid();
        var request = new { name = "Season", cropType = "RICE", startsOn = "2026-11-20", endsOn = "2026-11-01",
            prepareDays = 100, demandFactor = 0, items = new[] { new { storeProductId = id }, new { storeProductId = id } } };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/inventory/seasonal-rules", request, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/inventory/seasonal-rules?fromDate=2026-12-01&toDate=2026-11-01", Token)).StatusCode);
    }
    private sealed class PlanningStub : IReplenishmentService
    {
        public string? View { get; private set; }
        public ReplenishmentRequest? Request { get; private set; }
        public Task<ReplenishmentPage> GetAsync(string view, ReplenishmentRequest request, CancellationToken token)
        {
            View = view; Request = request;
            return Task.FromResult(new ReplenishmentPage([], request.Page, request.PageSize, 21,
                new(21, 5, 5, 1, 1, 2), DateTimeOffset.UtcNow, new(2026, 10, 1), new(2026, 10, 31), request.HorizonDays));
        }
    }

    [Fact]
    public async Task Delegated_planning_read_admits_sales_but_never_delivery_staff()
    {
        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(s => {
            s.RemoveAll<IReplenishmentService>(); s.AddSingleton<IReplenishmentService>(new PlanningStub());
            s.RemoveAll<IPermissionEvaluator>(); s.AddSingleton<IPermissionEvaluator>(new GrantedPlanning());
        }));
        using var sales = Client(app, "SALES_STAFF");
        using var delivery = Client(app, "DELIVERY_STAFF");
        Assert.Equal(HttpStatusCode.OK, (await sales.GetAsync("/api/inventory/replenishment/suggestions", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await delivery.GetAsync("/api/inventory/replenishment/suggestions", Token)).StatusCode);
    }
    private sealed class GrantedPlanning : IPermissionEvaluator
    {
        public Task<bool> HasAsync(string code, CancellationToken token) => Task.FromResult(code == "REPLENISHMENT.READ");
        public Task<CurrentPermissionsResponse> CurrentAsync(CancellationToken token) => Task.FromResult(new CurrentPermissionsResponse("SALES_STAFF", null, 0, null, ["REPLENISHMENT.READ"]));
    }
}
