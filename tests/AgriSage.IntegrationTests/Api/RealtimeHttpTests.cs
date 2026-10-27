using System.Net;
using System.Net.WebSockets;
using System.Text;
using AgriSage.Application.Features.Notifications;
using AgriSage.Application.Features.Permissions;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgriSage.IntegrationTests.Api;

public sealed class RealtimeHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    [Fact]
    public async Task Hub_negotiate_requires_a_valid_jwt_and_accepts_the_existing_bearer_contract()
    {
        using var client = factory.CreateClient();
        var token = TestContext.Current.CancellationToken;
        var path = "/api/realtime/negotiate?negotiateVersion=1";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(path, null, token)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(path, null, token)).StatusCode);
        var jwt = factory.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), "SALES_STAFF");
        client.DefaultRequestHeaders.Authorization = new("Bearer", jwt.Value);
        var result = await client.PostAsync(path, null, token);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("WebSockets", await result.Content.ReadAsStringAsync(token));
    }

    [Fact]
    public async Task Query_string_tokens_are_limited_to_the_hub_path()
    {
        var token = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();
        var jwt = factory.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), "SALES_STAFF");
        var encoded = Uri.EscapeDataString(jwt.Value);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/realtime/negotiate?negotiateVersion=1&access_token={encoded}", null, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/staff?access_token={encoded}", token)).StatusCode);
    }
    [Fact]
    public async Task Connected_users_only_receive_their_own_invalidation_without_business_payloads()
    {
        var token = TestContext.Current.CancellationToken;
        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPermissionEvaluator>();
            services.AddSingleton<IPermissionEvaluator, HubPermissions>();
        }));
        using var http = app.CreateClient();
        var issuer = app.Services.GetRequiredService<IAccessTokenService>();
        var userA = Guid.NewGuid(); var userB = Guid.NewGuid();
        var client = app.Server.CreateWebSocketClient();
        using var a = await client.ConnectAsync(new Uri("ws://localhost/api/realtime?access_token="
            + Uri.EscapeDataString(issuer.Issue(userA, "SALES_STAFF").Value)), token);
        using var b = await client.ConnectAsync(new Uri("ws://localhost/api/realtime?access_token="
            + Uri.EscapeDataString(issuer.Issue(userB, "SALES_STAFF").Value)), token);
        await Handshake(a, token); await Handshake(b, token);
        var eventId = Guid.NewGuid();
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IRealtimePublisher>().PublishAsync(new(eventId, "notifications", "user", userA), token);
        var text = await Receive(a, token);
        Assert.Contains(eventId.ToString(), text);
        Assert.Contains("notifications", text);
        Assert.DoesNotContain(userA.ToString(), text);
        var marker = Guid.NewGuid();
        await scope.ServiceProvider.GetRequiredService<IRealtimePublisher>().PublishAsync(new(marker, "notifications", "user", userB), token);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        string other;
        do
        {
            other = await Receive(b, timeout.Token);
            Assert.DoesNotContain(eventId.ToString(), other);
        } while (!other.Contains(marker.ToString(), StringComparison.Ordinal));
    }

    private static async Task Handshake(WebSocket socket, CancellationToken token)
    {
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, token);
        Assert.Contains("{}", await Receive(socket, token));
    }
    private static async Task<string> Receive(WebSocket socket, CancellationToken token)
    {
        var buffer = new byte[4096];
        var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }
    private sealed class HubPermissions : IPermissionEvaluator
    {
        public Task<bool> HasAsync(string code, CancellationToken token) => Task.FromResult(true);
        public Task<CurrentPermissionsResponse> CurrentAsync(CancellationToken token) => Task.FromResult(
            new CurrentPermissionsResponse("SALES_STAFF", Guid.NewGuid(), 0, 0, []));
    }
}
