using System.Net;
using System.Net.Http.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

public sealed class AuthOperationsHttpTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [Theory]
    [InlineData("GET", "/api/auth/sessions")]
    [InlineData("POST", "/api/auth/logout")]
    [InlineData("POST", "/api/auth/logout-all")]
    [InlineData("POST", "/api/auth/email-verification/request")]
    [InlineData("POST", "/api/auth/phone-verification/request")]
    [InlineData("GET", "/api/me/notifications")]
    [InlineData("GET", "/api/me/notifications/unread-count")]
    [InlineData("POST", "/api/me/notifications/read-all")]
    [InlineData("GET", "/api/audit-logs")]
    public async Task Private_routes_require_authentication(string method, string path)
    {
        using var factory = new ApiStartupTests.DevelopmentApiFactory(); using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request, Token)).StatusCode);
    }
    [Theory]
    [InlineData("/api/auth/refresh", "{\"refreshToken\":\"bad\"}")]
    [InlineData("/api/auth/forgot-password", "{\"identifier\":\"\"}")]
    [InlineData("/api/auth/reset-password", "{\"token\":\"bad\",\"newPassword\":\"short\"}")]
    public async Task Anonymous_requests_validate_before_any_database_query(string path, string json)
    {
        using var factory = new ApiStartupTests.DevelopmentApiFactory(); using var client = factory.CreateClient();
        var response = await client.PostAsync(path, new StringContent(json, System.Text.Encoding.UTF8, "application/json"), Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
    [Theory]
    [InlineData("FARMER")]
    [InlineData("SALES_STAFF")]
    [InlineData("DELIVERY_STAFF")]
    public async Task Operational_roles_cannot_read_audit_history(string role)
    {
        using var factory = new StaffHttpTests.StaffApiFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role).Value);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/audit-logs", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/audit-logs/{Guid.NewGuid()}", Token)).StatusCode);
    }
    [Theory]
    [InlineData("/api/me/notifications?status=BOGUS")]
    [InlineData("/api/me/notifications?pageSize=101")]
    [InlineData("/api/audit-logs?page=0")]
    [InlineData("/api/audit-logs?from=2026-10-08T00%3A00%3A00Z&to=2026-10-07T00%3A00%3A00Z")]
    public async Task List_filters_are_validated(string path)
    {
        using var factory = new StaffHttpTests.StaffApiFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), "ADMIN").Value);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path, Token)).StatusCode);
    }
    [Fact]
    public async Task Otp_format_is_validated_and_missing_email_transport_is_unavailable()
    {
        using var factory = new StaffHttpTests.StaffApiFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), "FARMER").Value);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/phone-verification/confirm", new { code = "12" }, Token)).StatusCode);
        // Deliberately unconfigured transport; no database needed.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsync("/api/auth/email-verification/request", null, Token)).StatusCode);
    }
}
