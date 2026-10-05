using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Returns;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgriSage.IntegrationTests.Api;

public class RefundHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static readonly Guid Id = Guid.NewGuid();
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public static TheoryData<string, string> Routes => new()
    {
        { "POST", "/api/returns/{id}/refunds" }, { "POST", "/api/returns/{id}/refunds/{refundId}/complete" },
        { "POST", "/api/returns/{id}/refunds/{refundId}/fail" }, { "POST", "/api/returns/{id}/refunds/{refundId}/cancel" },
        { "GET", "/api/orders/{id}/refunds" }, { "POST", "/api/orders/{id}/refunds" },
        { "POST", "/api/orders/{id}/refunds/{refundId}/complete" }, { "POST", "/api/orders/{id}/refunds/{refundId}/fail" },
        { "POST", "/api/orders/{id}/refunds/{refundId}/cancel" }
    };
    private static HttpClient Client(WebApplicationFactory<Program> app, string? role)
    {
        var client = app.CreateClient();
        if (role is not null) client.DefaultRequestHeaders.Authorization = new("Bearer", app.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role).Value);
        return client;
    }
    private static string Url(string route) => route.Replace("{id}", Id.ToString()).Replace("{refundId}", Id.ToString());
    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Every_refund_route_requires_management_role(string method, string route)
    {
        foreach (var role in new string?[] { null, "FARMER", "SALES_STAFF", "DELIVERY_STAFF" })
        {
            using var client = Client(factory, role); using var request = new HttpRequestMessage(new HttpMethod(method), Url(route));
            Assert.Equal(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, (await client.SendAsync(request, Token)).StatusCode);
        }
    }
    [Theory]
    [InlineData("ADMIN")]
    [InlineData("STORE_OWNER")]
    public async Task Management_reaches_validation_for_invalid_refund_input(string role)
    {
        using var client = Client(factory, role);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/returns/{Id}/refunds", new RefundRequest("CASH", 1.001m), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/returns/{Id}/refunds", new RefundRequest("PAYOS", 1), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/orders/{Id}/refunds", new OrderRefundRequest(Guid.Empty, "CASH", 1), Token)).StatusCode);
        foreach (var root in new[] { "orders", "returns" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/{root}/{Id}/refunds/{Id}/complete", new CompleteRefundRequest(ProofFileUrl: "http://unsafe.test/photo"), Token)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/{root}/{Id}/refunds/{Id}/cancel", new CancelRefundRequest(" "), Token)).StatusCode);
        }
    }
    [Fact]
    public async Task Authorized_routes_return_shared_refund_shape_locations_and_all_actions_are_in_swagger()
    {
        await using var app = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IRefundService>(); services.AddSingleton<IRefundService>(new Stub());
        }));
        using var client = Client(app, "STORE_OWNER");
        foreach (var root in new[] { "returns", "orders" })
        {
            var request = root == "returns" ? (object)new RefundRequest("CASH", 100, Id) : new OrderRefundRequest(Id, "CASH", 100);
            var created = await client.PostAsJsonAsync($"/api/{root}/{Id}/refunds", request, Token);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.EndsWith(root == "returns" ? $"/api/returns/{Id}" : $"/api/orders/{Id}/refunds", created.Headers.Location!.ToString());
            var body = await created.Content.ReadFromJsonAsync<RefundResponse>(Token); Assert.Equal("PENDING", body!.Status); Assert.Equal(100m, body.Amount);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/{root}/{Id}/refunds/{Id}/complete", new CompleteRefundRequest(), Token)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/{root}/{Id}/refunds/{Id}/fail", new FailRefundRequest("failed"), Token)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/{root}/{Id}/refunds/{Id}/cancel", new CancelRefundRequest("cancel"), Token)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/orders/{Id}/refunds", Token)).StatusCode);
        using var json = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token)); var paths = json.RootElement.GetProperty("paths");
        foreach (var row in Routes) Assert.True(paths.TryGetProperty(row.Data.Item2, out var path) && path.TryGetProperty(row.Data.Item1.ToLowerInvariant(), out _));
    }
    private sealed class Stub : IRefundService
    {
        private static RefundResponse Response(string source = "SALES_RETURN") => new(Id, "RF-test", source, source == "SALES_RETURN" ? Id : null,
            source == "ORDER" ? Id : null, Id, "CASH", 100, "PENDING", null, null, Id, DateTimeOffset.UtcNow, null, null, null, null, null, null);
        public Task<RefundResponse> CreateReturnAsync(Guid id, RefundRequest request, CancellationToken token) => Task.FromResult(Response());
        public Task<RefundResponse> CompleteReturnAsync(Guid id, Guid rid, CompleteRefundRequest request, CancellationToken token) => Task.FromResult(Response());
        public Task<RefundResponse> FailReturnAsync(Guid id, Guid rid, FailRefundRequest request, CancellationToken token) => Task.FromResult(Response());
        public Task<RefundResponse> CancelReturnAsync(Guid id, Guid rid, CancelRefundRequest request, CancellationToken token) => Task.FromResult(Response());
        public Task<IReadOnlyList<RefundResponse>> ListOrderAsync(Guid id, CancellationToken token) => Task.FromResult<IReadOnlyList<RefundResponse>>([Response("ORDER")]);
        public Task<RefundResponse> CreateOrderAsync(Guid id, OrderRefundRequest request, CancellationToken token) => Task.FromResult(Response("ORDER"));
        public Task<RefundResponse> CompleteOrderAsync(Guid id, Guid rid, CompleteRefundRequest request, CancellationToken token) => Task.FromResult(Response("ORDER"));
        public Task<RefundResponse> FailOrderAsync(Guid id, Guid rid, FailRefundRequest request, CancellationToken token) => Task.FromResult(Response("ORDER"));
        public Task<RefundResponse> CancelOrderAsync(Guid id, Guid rid, CancelRefundRequest request, CancellationToken token) => Task.FromResult(Response("ORDER"));
    }
}
