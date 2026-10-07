using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgriSage.IntegrationTests.Api;

// Authorization and validation of the staff API without a database: the account-status check is replaced by a fake.
public class StaffHttpTests : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private readonly StaffApiFactory _factory;

    public StaffHttpTests(StaffApiFactory factory)
    {
        _factory = factory;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private HttpClient ClientFor(string? role, Guid? userId = null)
    {
        var client = _factory.CreateClient();
        if (role is not null)
        {
            var token = _factory.Services.GetRequiredService<IAccessTokenService>().Issue(userId ?? Guid.NewGuid(), role);
            client.DefaultRequestHeaders.Authorization = new("Bearer", token.Value);
        }

        return client;
    }

    [Fact]
    public async Task Staff_api_requires_a_token()
    {
        using var client = ClientFor(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/staff", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/staff", new { }, Token)).StatusCode);
    }

    [Theory]
    [InlineData("FARMER")]
    [InlineData("DELIVERY_STAFF")]
    public async Task Other_roles_cannot_use_the_staff_api(string role)
    {
        using var client = ClientFor(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/staff", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/staff", new { }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/staff/{Guid.NewGuid()}", Token)).StatusCode);
    }

    // Sales staff assign drivers to deliveries, so the list is open to them (delivery staff only, enforced by the service);
    // every other staff action stays with Admin and Store Owner. A bad query is rejected with 400 before any database
    // access, which proves the request got past authorization (a 403 would come first).
    [Fact]
    public async Task Sales_staff_can_list_staff_but_not_manage_it()
    {
        using var client = ClientFor("SALES_STAFF");
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/staff?page=0", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/staff/{id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/staff", new { }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/staff/{id}", new { }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/staff/{id}/lock", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/staff/{id}/unlock", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/staff/{id}/reset-password", new { }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/staff/{id}", Token)).StatusCode);
    }

    [Fact]
    public async Task Create_with_bad_data_returns_field_errors()
    {
        using var client = ClientFor("ADMIN");

        var response = await client.PostAsJsonAsync(
            "/api/staff", new { fullName = "Nguyen Van B", role = "ADMIN", password = "short" }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("role", out _));
        Assert.True(errors.TryGetProperty("password", out _));
        Assert.True(errors.TryGetProperty("contact", out _));
    }

    [Theory]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=0")]
    [InlineData("role=FARMER")]
    [InlineData("status=BOGUS")]
    public async Task List_rejects_invalid_query_parameters(string query)
    {
        using var client = ClientFor("STORE_OWNER");

        var response = await client.GetAsync($"/api/staff?{query}", Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Id_in_the_route_must_be_a_guid()
    {
        using var client = ClientFor("ADMIN");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/staff/not-a-guid", Token)).StatusCode);
    }

    [Fact]
    public async Task Change_password_requires_a_token_and_a_valid_body()
    {
        using var anonymous = ClientFor(null);
        using var farmer = ClientFor("FARMER");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/auth/change-password", new { }, Token)).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await farmer.PostAsJsonAsync(
                "/api/auth/change-password", new { currentPassword = "x", newPassword = "short" }, Token)).StatusCode);
    }

    [Fact]
    public async Task A_valid_token_of_an_inactive_account_is_rejected_immediately()
    {
        var lockedUser = Guid.NewGuid();
        _factory.Accounts.Inactive.Add(lockedUser);
        using var locked = ClientFor("ADMIN", lockedUser);
        using var active = ClientFor("DELIVERY_STAFF");

        Assert.Equal(HttpStatusCode.Unauthorized, (await locked.GetAsync("/api/staff", Token)).StatusCode);
        // Same token shape for an active account gets past authentication (403: the role is not allowed).
        Assert.Equal(HttpStatusCode.Forbidden, (await active.GetAsync("/api/staff", Token)).StatusCode);
    }

    [Fact]
    public async Task Swagger_lists_the_staff_endpoints()
    {
        using var client = ClientFor(null);

        var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token));
        var paths = document.RootElement.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/api/staff", out _));
        Assert.True(paths.TryGetProperty("/api/staff/{id}", out _));
        Assert.True(paths.TryGetProperty("/api/staff/{id}/lock", out _));
        Assert.True(paths.TryGetProperty("/api/staff/{id}/unlock", out _));
        Assert.True(paths.TryGetProperty("/api/staff/{id}/reset-password", out _));
        Assert.True(paths.TryGetProperty("/api/auth/change-password", out _));
    }

    public sealed class FakeAccounts : IUserAccessValidator
    {
        public HashSet<Guid> Inactive { get; } = [];

        public Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(!Inactive.Contains(userId));
    }

    public sealed class StaffApiFactory : WebApplicationFactory<Program>
    {
        public FakeAccounts Accounts { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            // The developer's own appsettings.Local.json (payOS keys, PayOS:Mode=Simulated...) must not change what these tests assert.
            builder.UseSetting(AgriSage.Api.Extensions.LocalSettingsExtensions.DisabledSetting, "true");
            // The in-process HTTP test host does not write machine-wide Windows event logs.
            builder.UseSetting("Logging:EventLog:LogLevel:Default", "None");
            // Test-only key; real keys come from User Secrets / environment variables.
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-not-a-secret-000000");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUserAccessValidator>();
                services.AddSingleton<IUserAccessValidator>(Accounts);
            });
        }
    }
}
