using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AgriSage.IntegrationTests.Api;

// HTTP behaviour that never reaches the database: validation, authentication and routing.
public class AuthHttpTests : IClassFixture<ApiStartupTests.DevelopmentApiFactory>
{
    private readonly ApiStartupTests.DevelopmentApiFactory _factory;

    public AuthHttpTests(ApiStartupTests.DevelopmentApiFactory factory)
    {
        _factory = factory;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Me_without_a_token_is_unauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me", Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_with_a_garbage_token_is_unauthorized()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "not.a.jwt");

        var response = await client.GetAsync("/api/auth/me", Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_without_phone_or_email_and_with_a_short_password_returns_validation_errors()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new { fullName = "Nguyen Van A", password = "short" }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var errors = (await Body(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("password", out _));
        Assert.True(errors.TryGetProperty("contact", out _));
    }

    [Fact]
    public async Task Register_with_an_invalid_phone_is_rejected_before_any_database_access()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { fullName = "Nguyen Van A", phoneNumber = "12345", password = "password1" },
            Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await Body(response)).GetProperty("errors").TryGetProperty("phoneNumber", out _));
    }

    [Fact]
    public async Task Login_with_missing_fields_is_a_bad_request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { identifier = "", password = "" }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_lists_the_three_auth_endpoints()
    {
        using var client = _factory.CreateClient();

        var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token));
        var paths = document.RootElement.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/api/auth/register", out _));
        Assert.True(paths.TryGetProperty("/api/auth/login", out _));
        Assert.True(paths.TryGetProperty("/api/auth/me", out _));
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.Clone();
}

// Separate fixture: the rate limiter keeps its counters per application instance.
public class AuthRateLimitTests : IClassFixture<ApiStartupTests.DevelopmentApiFactory>
{
    private readonly ApiStartupTests.DevelopmentApiFactory _factory;

    public AuthRateLimitTests(ApiStartupTests.DevelopmentApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_attempts_are_limited_per_client()
    {
        var token = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new { identifier = "", password = "" }, token);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var limited = await client.PostAsJsonAsync("/api/auth/login", new { identifier = "", password = "" }, token);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
    }
}
