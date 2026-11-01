using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// Authorization and validation of the advisory APIs without a database (api-flows/ADVISORY.md sections 5-8): a request that
// gets past the role and permission gates is stopped by validation (400) before any database access, which proves the
// gates let it through (a 403 would come first).
public sealed class AdvisoryHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly Guid Id = Guid.NewGuid();

    private HttpClient ClientFor(string? role)
    {
        var client = factory.CreateClient();
        if (role is not null)
        {
            var token = factory.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role);
            client.DefaultRequestHeaders.Authorization = new("Bearer", token.Value);
        }

        return client;
    }

    public static TheoryData<string> AllRoutes =>
    [
        "GET /api/me/advisory/problems",
        "POST /api/me/advisory/recommendations",
        "POST /api/diseases",
        $"POST /api/diseases/{Id}/activate",
        $"POST /api/diseases/{Id}/deactivate",
        $"GET /api/diseases/{Id}/products",
        $"POST /api/diseases/{Id}/products",
        $"PUT /api/disease-products/{Id}",
        $"POST /api/disease-products/{Id}/approve",
        $"DELETE /api/disease-products/{Id}",
        "GET /api/advisory/sessions"
    ];

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task Every_advisory_route_requires_a_token(string route)
    {
        using var client = ClientFor(null);
        var (method, path) = Split(route);

        var response = await client.SendAsync(new HttpRequestMessage(method, path) { Content = JsonContent.Create(new { }) }, Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("STORE_OWNER")]
    [InlineData("SALES_STAFF")]
    [InlineData("DELIVERY_STAFF")]
    [InlineData("ADMIN")]
    public async Task Only_a_farmer_uses_the_farmer_routes(string role)
    {
        using var client = ClientFor(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/me/advisory/problems", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/me/advisory/recommendations", new { diseaseId = Id }, Token)).StatusCode);
    }

    [Theory]
    [InlineData("FARMER")]
    [InlineData("DELIVERY_STAFF")]
    public async Task Farmers_and_drivers_cannot_reach_the_store_routes(string role)
    {
        using var client = ClientFor(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/diseases/{Id}/products", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/advisory/sessions", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/diseases", new { }, Token)).StatusCode);
    }

    // Sales staff may read the links and the sessions but never create, change, approve or switch anything off.
    [Fact]
    public async Task Sales_staff_cannot_change_problems_or_links_or_approve()
    {
        using var client = ClientFor("SALES_STAFF");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/diseases", new { }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/diseases/{Id}/deactivate", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/diseases/{Id}/products", new { }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/disease-products/{Id}", new { }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/disease-products/{Id}/approve", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/disease-products/{Id}", Token)).StatusCode);
    }

    [Fact]
    public async Task A_farmer_reaches_validation_of_the_search_and_the_recommendation()
    {
        using var client = ClientFor("FARMER");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/me/advisory/problems?page=0", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/me/advisory/problems?type=WEEDS", Token)).StatusCode);

        var response = await client.PostAsJsonAsync(
            "/api/me/advisory/recommendations",
            new { diseaseId = Guid.Empty, areaSqm = -5, growthStage = "FLOWERING", queryText = new string('x', 201) },
            Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = Errors(await response.Content.ReadAsStringAsync(Token));
        foreach (var field in new[] { "diseaseId", "areaSqm", "growthStage", "queryText" })
        {
            Assert.True(errors.TryGetProperty(field, out _), field);
        }

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/me/advisory/recommendations",
                new { diseaseId = Id, areaSqm = 1_000_001 }, Token)).StatusCode);
    }

    [Fact]
    public async Task A_new_problem_needs_a_valid_code_name_and_type()
    {
        using var client = ClientFor("STORE_OWNER");

        var response = await client.PostAsJsonAsync("/api/diseases", new { code = "bad code", name = "", problemType = "FUNGUS" }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = Errors(await response.Content.ReadAsStringAsync(Token));
        foreach (var field in new[] { "code", "name", "problemType" })
        {
            Assert.True(errors.TryGetProperty(field, out _), field);
        }
    }

    [Fact]
    public async Task A_link_needs_a_group_a_dose_and_sane_numbers()
    {
        using var client = ClientFor("ADMIN");

        var response = await client.PostAsJsonAsync(
            $"/api/diseases/{Id}/products",
            new
            {
                productId = Guid.Empty, recommendationGroup = "MEDICINE", doseText = "", dosePerHaBase = 0, applications = 11,
                preHarvestDays = -1, growthStages = new[] { "FLOWERING" }
            },
            Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = Errors(await response.Content.ReadAsStringAsync(Token));
        foreach (var field in new[] { "productId", "recommendationGroup", "doseText", "dosePerHaBase", "applications", "preHarvestDays" })
        {
            Assert.True(errors.TryGetProperty(field, out _), field);
        }
    }

    [Theory]
    [InlineData("SALES_STAFF")]
    [InlineData("STORE_OWNER")]
    [InlineData("ADMIN")]
    public async Task Store_staff_reach_validation_of_the_session_list(string role)
    {
        using var client = ClientFor(role);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/advisory/sessions?page=0", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/advisory/sessions?from=2026-11-10&to=2026-11-01", Token)).StatusCode);
    }

    [Fact]
    public async Task The_swagger_document_lists_the_advisory_routes()
    {
        using var client = ClientFor(null);

        var paths = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token))
            .RootElement.GetProperty("paths");

        foreach (var path in new[]
                 {
                     "/api/me/advisory/problems", "/api/me/advisory/recommendations", "/api/diseases",
                     "/api/diseases/{id}/activate", "/api/diseases/{id}/deactivate", "/api/diseases/{id}/products",
                     "/api/disease-products/{id}", "/api/disease-products/{id}/approve", "/api/advisory/sessions"
                 })
        {
            Assert.True(paths.TryGetProperty(path, out _), path);
        }
    }

    private static (HttpMethod Method, string Path) Split(string route)
    {
        var parts = route.Split(' ', 2);

        return (new HttpMethod(parts[0]), parts[1]);
    }

    private static JsonElement Errors(string body) => JsonDocument.Parse(body).RootElement.GetProperty("errors");
}
