using System.Net;
using AgriSage.Api.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AgriSage.IntegrationTests.Api;

// CORS for the browser clients: only the configured origins, preflight answered, headers also on error responses.
// Development lists http://localhost:5173 and http://localhost:3000 (appsettings.Development.json).
public class CorsHttpTests : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private const string Allowed = "http://localhost:5173";

    private readonly StaffHttpTests.StaffApiFactory _factory;

    public CorsHttpTests(StaffHttpTests.StaffApiFactory factory)
    {
        _factory = factory;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static HttpRequestMessage Preflight(string origin, string path = "/api/orders") =>
        new(HttpMethod.Options, path)
        {
            Headers =
            {
                { "Origin", origin },
                { "Access-Control-Request-Method", "POST" },
                { "Access-Control-Request-Headers", "authorization,content-type" }
            }
        };

    [Fact]
    public async Task A_preflight_from_a_configured_origin_is_answered_with_that_origin_and_the_needed_headers()
    {
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(Preflight(Allowed), Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(Allowed, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains("authorization", string.Join(',', response.Headers.GetValues("Access-Control-Allow-Headers")), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("POST", string.Join(',', response.Headers.GetValues("Access-Control-Allow-Methods")));
        Assert.Equal("600", Assert.Single(response.Headers.GetValues("Access-Control-Max-Age")));
        // Bearer tokens travel in a header; no cookies, so credentials are not allowed.
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://localhost:5174")]
    [InlineData("https://localhost:5173")]
    public async Task Other_origins_get_no_cors_headers(string origin)
    {
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(Preflight(origin), Token);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Methods"));
    }

    [Fact]
    public async Task An_error_response_from_an_allowed_origin_still_carries_the_header_so_the_browser_can_read_it()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/orders") { Headers = { { "Origin", Allowed } } };

        var unauthorized = await client.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(Allowed, Assert.Single(unauthorized.Headers.GetValues("Access-Control-Allow-Origin")));

        using var missing = new HttpRequestMessage(HttpMethod.Get, "/api/no-such-route") { Headers = { { "Origin", Allowed } } };
        var notFound = await client.SendAsync(missing, Token);
        Assert.Equal(Allowed, Assert.Single(notFound.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task A_request_without_an_origin_is_untouched()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/orders", Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private sealed class FactoryWith(string origin) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-not-a-secret-000000");
            builder.UseSetting("Cors:AllowedOrigins:0", origin);
        }
    }

    [Fact]
    public async Task Origins_set_by_configuration_replace_the_listed_ones_and_a_wrong_value_stops_the_start_up()
    {
        await using (var custom = new FactoryWith("https://app.agrisage.example/"))
        {
            using var client = custom.CreateClient();

            var response = await client.SendAsync(Preflight("https://app.agrisage.example"), Token);

            Assert.Equal("https://app.agrisage.example", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
            // The first entry was replaced; the second (localhost:3000) is still there.
            Assert.False((await client.SendAsync(Preflight(Allowed), Token)).Headers.Contains("Access-Control-Allow-Origin"));
        }

        await using var wildcard = new FactoryWith("*");
        var failure = Assert.ThrowsAny<Exception>(() => wildcard.CreateClient());
        Assert.Contains("Cors:AllowedOrigins", failure.ToString());
    }

    [Fact]
    public void Origins_are_trimmed_deduplicated_and_must_be_plain_http_or_https_origins()
    {
        Assert.Equal(
            ["http://localhost:5173", "https://app.example.com:8443"],
            CorsExtensions.ParseOrigins([" http://localhost:5173/ ", "HTTP://LOCALHOST:5173", "", null, "https://app.example.com:8443"]));
        Assert.Empty(CorsExtensions.ParseOrigins([]));
        Assert.Empty(CorsExtensions.ParseOrigins(["  ", ""]));

        foreach (var wrong in new[] { "*", "https://*.example.com", "app.example.com", "ftp://example.com", "https://example.com/app", "https://example.com?x=1", "https://user@example.com", "https://example.com#top", "/relative" })
        {
            var exception = Assert.Throws<InvalidOperationException>(() => CorsExtensions.ParseOrigins([wrong]));
            Assert.Contains("Cors:AllowedOrigins", exception.Message);
        }
    }
}
