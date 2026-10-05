using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Returns;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgriSage.IntegrationTests.Api;

public class SalesReturnHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static readonly Guid Id = Guid.NewGuid();
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public static TheoryData<string, string> StaffRoutes => new()
    {
        { "GET", "/api/orders/{id}/returnable" }, { "POST", "/api/returns" }, { "GET", "/api/returns" },
        { "GET", "/api/returns/{id}" }, { "POST", "/api/returns/{id}/items" }, { "DELETE", "/api/returns/{id}/items/{itemId}" },
        { "POST", "/api/returns/{id}/approve" }, { "POST", "/api/returns/{id}/reject" }, { "POST", "/api/returns/{id}/cancel" },
        { "POST", "/api/returns/{id}/receive" }, { "PUT", "/api/returns/{id}/items/{itemId}/inspection" }, { "POST", "/api/returns/{id}/complete-inspection" }
    };
    public static TheoryData<string, string> FarmerRoutes => new()
    {
        { "GET", "/api/me/orders/{id}/returnable" }, { "POST", "/api/me/returns" }, { "GET", "/api/me/returns" },
        { "GET", "/api/me/returns/{id}" }, { "POST", "/api/me/returns/{id}/cancel" }
    };
    private static string Url(string path) => path.Replace("{id}", Id.ToString()).Replace("{itemId}", Id.ToString());
    private static HttpClient Client(WebApplicationFactory<Program> app, string? role)
    {
        var client = app.CreateClient();
        if (role is not null)
            client.DefaultRequestHeaders.Authorization = new("Bearer", app.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role).Value);
        return client;
    }
    [Theory]
    [MemberData(nameof(StaffRoutes))]
    public async Task Staff_routes_reject_anonymous_farmers_and_delivery_staff(string method, string route)
    {
        foreach (var role in new string?[] { null, "FARMER", "DELIVERY_STAFF" })
        {
            using var client = Client(factory, role); using var request = new HttpRequestMessage(new HttpMethod(method), Url(route));
            Assert.Equal(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, (await client.SendAsync(request, Token)).StatusCode);
        }
    }
    [Theory]
    [MemberData(nameof(FarmerRoutes))]
    public async Task Farmer_routes_reject_anonymous_and_all_staff_roles(string method, string route)
    {
        foreach (var role in new string?[] { null, "ADMIN", "STORE_OWNER", "SALES_STAFF", "DELIVERY_STAFF" })
        {
            using var client = Client(factory, role); using var request = new HttpRequestMessage(new HttpMethod(method), Url(route));
            Assert.Equal(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, (await client.SendAsync(request, Token)).StatusCode);
        }
    }
    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("complete-inspection")]
    public async Task Sales_cannot_manage_returns(string action)
    {
        using var client = Client(factory, "SALES_STAFF");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/returns/{Id}/{action}", new { reason = "Test" }, Token)).StatusCode);
    }
    [Theory]
    [InlineData("ADMIN")]
    [InlineData("STORE_OWNER")]
    [InlineData("SALES_STAFF")]
    public async Task Invalid_requests_fail_validation_before_service_access(string role)
    {
        using var client = Client(factory, role);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/returns", new { orderId = Id, items = Array.Empty<object>() }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/returns/{Id}/items", new ReturnItemRequest(Id, 0, "OTHER"), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/returns/{Id}/items/{Id}/inspection", new ReturnInspectionRequest("PENDING_INSPECTION"), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/returns/{Id}/cancel", new ReturnReasonRequest(" "), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/returns?status=BAD&pageSize=0", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/returns?fromDate=0001-01-01", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/returns?toDate=0001-01-01", Token)).StatusCode);
    }
    [Fact]
    public async Task All_authorized_routes_return_expected_status_and_created_locations()
    {
        var stub = new Stub();
        await using var app = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<ISalesReturnService>(); services.AddSingleton<ISalesReturnService>(stub);
            services.RemoveAll<IMySalesReturnService>(); services.AddSingleton<IMySalesReturnService>(stub);
        }));
        using var staff = Client(app, "STORE_OWNER"); using var farmer = Client(app, "FARMER");
        var line = new ReturnItemRequest(Id, 3, "OTHER", OriginalStockMovementItemId: Id);
        foreach (var (client, root) in new[] { (staff, "/api/returns"), (farmer, "/api/me/returns") })
        {
            var response = await client.PostAsJsonAsync(root, new CreateReturnRequest(Id, [line]), Token);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.EndsWith($"{root}/{Id}", response.Headers.Location!.ToString());
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(root, Token)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{root}/{Id}", Token)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"{root}/{Id}/cancel", new ReturnReasonRequest("Test"), Token)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"/api/orders/{Id}/returnable", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await farmer.GetAsync($"/api/me/orders/{Id}/returnable", Token)).StatusCode);
        foreach (var action in new[] { "approve", "receive", "complete-inspection" })
            Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync($"/api/returns/{Id}/{action}", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync($"/api/returns/{Id}/reject", new ReturnReasonRequest("Test"), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync($"/api/returns/{Id}/items", line, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.DeleteAsync($"/api/returns/{Id}/items/{Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.PutAsJsonAsync($"/api/returns/{Id}/items/{Id}/inspection", new ReturnInspectionRequest("RESELLABLE"), Token)).StatusCode);
        using var json = JsonDocument.Parse(await staff.GetStringAsync("/swagger/v1/swagger.json", Token));
        var paths = json.RootElement.GetProperty("paths");
        foreach (var row in StaffRoutes.Concat(FarmerRoutes))
            Assert.True(paths.TryGetProperty(row.Data.Item2, out var path) && path.TryGetProperty(row.Data.Item1.ToLowerInvariant(), out _));
    }
    private sealed class Stub : ISalesReturnService, IMySalesReturnService
    {
        private static Task<SalesReturnResponse> Response() => Task.FromResult(new SalesReturnResponse(Id, Id, "RT-test", Id, "OD-test", Id,
            "Test", "REQUESTED", Id, DateTimeOffset.UtcNow, null, null, null, null, null, null, null, null, null, null, null, null, 0, 0, 0, [], []));
        public Task<ReturnableResponse> ReturnableAsync(Guid id, CancellationToken token) => Task.FromResult(new ReturnableResponse(id, "OD-test", "PICKUP", []));
        public Task<SalesReturnResponse> CreateAsync(CreateReturnRequest request, CancellationToken token) => Response();
        public Task<SalesReturnResponse> GetAsync(Guid id, CancellationToken token) => Response();
        public Task<PagedResult<SalesReturnListItem>> ListAsync(SalesReturnListRequest request, CancellationToken token) => Task.FromResult(new PagedResult<SalesReturnListItem>([], 1, 20, 0));
        public Task<PagedResult<SalesReturnListItem>> ListAsync(PaginationRequest request, CancellationToken token) => Task.FromResult(new PagedResult<SalesReturnListItem>([], 1, 20, 0));
        public Task<SalesReturnResponse> AddItemAsync(Guid id, ReturnItemRequest request, CancellationToken token) => Response();
        public Task<SalesReturnResponse> RemoveItemAsync(Guid id, Guid item, CancellationToken token) => Response();
        public Task<SalesReturnResponse> ApproveAsync(Guid id, CancellationToken token) => Response();
        public Task<SalesReturnResponse> RejectAsync(Guid id, ReturnReasonRequest request, CancellationToken token) => Response();
        public Task<SalesReturnResponse> CancelAsync(Guid id, ReturnReasonRequest request, CancellationToken token) => Response();
        public Task<SalesReturnResponse> ReceiveAsync(Guid id, CancellationToken token) => Response();
        public Task<SalesReturnResponse> InspectAsync(Guid id, Guid item, ReturnInspectionRequest request, CancellationToken token) => Response();
        public Task<SalesReturnResponse> CompleteInspectionAsync(Guid id, CancellationToken token) => Response();
    }
}
