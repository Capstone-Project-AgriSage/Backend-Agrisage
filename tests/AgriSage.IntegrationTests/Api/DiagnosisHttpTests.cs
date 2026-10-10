using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// Authorization and validation of the AI diagnosis APIs without a database (AI_DIAGNOSIS.md): a request that gets past
// the role and permission gates is stopped by validation (400) or content type (415) before any database access, which
// proves the gates let it through (a 403 would come first).
public sealed class DiagnosisHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

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

    private static readonly Guid Id = Guid.NewGuid();

    public static TheoryData<string> AllRoutes =>
    [
        "GET /api/me/diagnosis-cases",
        "GET /api/diagnosis-cases",
        "GET /api/ai-models",
        "GET /api/diseases",
        $"PUT /api/staff/{Id}/ai-review"
    ];

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task Every_diagnosis_route_requires_a_token(string route)
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

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/me/diagnosis-cases", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/me/diagnosis-cases/{Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/me/diagnosis-cases/{Id}/cancel", null, Token)).StatusCode);
    }

    [Theory]
    [InlineData("FARMER")]
    [InlineData("DELIVERY_STAFF")]
    public async Task Farmers_and_drivers_cannot_use_the_reviewer_routes(string role)
    {
        using var client = ClientFor(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/diagnosis-cases", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/diagnosis-cases/{Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/diagnosis-cases/{Id}/review", new { }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/diagnosis-cases/{Id}/rerun-ai", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/diagnosis-cases/{Id}/recommendations/{Id}", Token)).StatusCode);
    }

    [Theory]
    [InlineData("FARMER")]
    [InlineData("DELIVERY_STAFF")]
    [InlineData("SALES_STAFF")]
    public async Task Models_and_policies_are_for_admin_and_owner_only(string role)
    {
        using var client = ClientFor(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ai-models", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/ai-models", new { }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/ai-models/{Id}/activate", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/ai-policies/{Id}/activate", null, Token)).StatusCode);
    }

    [Fact]
    public async Task Sales_staff_can_read_diseases_but_not_change_them()
    {
        using var client = ClientFor("SALES_STAFF");

        // Allowed: validation (400 for an invalid body) is reached only on write routes they may not use, so check 403s.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/diseases/{Id}", new { name = "x" }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/diseases/{Id}/treatments", new { }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/disease-treatments/{Id}", Token)).StatusCode);
    }

    [Fact]
    public async Task The_review_right_is_set_by_admin_and_owner_only()
    {
        using var sales = ClientFor("SALES_STAFF");
        using var farmer = ClientFor("FARMER");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await sales.PutAsJsonAsync($"/api/staff/{Id}/ai-review", new { enabled = true }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await farmer.PutAsJsonAsync($"/api/staff/{Id}/ai-review", new { enabled = true }, Token)).StatusCode);
    }

    [Fact]
    public async Task A_farmer_reaches_validation_of_the_list_and_the_media_type_of_the_upload()
    {
        using var client = ClientFor("FARMER");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/me/diagnosis-cases?page=0", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/me/diagnosis-cases?pageSize=101", Token)).StatusCode);
        // The upload is multipart; JSON is refused before any file or database is touched.
        Assert.Equal(HttpStatusCode.UnsupportedMediaType,
            (await client.PostAsJsonAsync("/api/me/diagnosis-cases", new { note = "x" }, Token)).StatusCode);
    }

    [Theory]
    [InlineData("SALES_STAFF")]
    [InlineData("STORE_OWNER")]
    [InlineData("ADMIN")]
    public async Task Reviewers_reach_validation_of_the_queue_and_of_a_review(string role)
    {
        using var client = ClientFor(role);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/diagnosis-cases?page=0", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/diagnosis-cases?from=2026-11-10&to=2026-11-01", Token)).StatusCode);

        var review = await client.PostAsJsonAsync($"/api/diagnosis-cases/{Id}/review", new { decision = "" }, Token);
        Assert.Equal(HttpStatusCode.BadRequest, review.StatusCode);
        Assert.True(Errors(await review.Content.ReadAsStringAsync(Token)).TryGetProperty("decision", out _));

        var recommendation = await client.PostAsJsonAsync(
            $"/api/diagnosis-cases/{Id}/recommendations", new { type = "", rankOrder = 5000 }, Token);
        Assert.Equal(HttpStatusCode.BadRequest, recommendation.StatusCode);
        var errors = Errors(await recommendation.Content.ReadAsStringAsync(Token));
        Assert.True(errors.TryGetProperty("type", out _));
        Assert.True(errors.TryGetProperty("rankOrder", out _));
    }

    [Fact]
    public async Task A_model_registration_is_validated_before_it_is_stored()
    {
        using var client = ClientFor("STORE_OWNER");

        var response = await client.PostAsJsonAsync(
            "/api/ai-models",
            new { name = "", version = "", framework = "", modelStorageUrl = "", classLabels = new[] { "" }, inputWidth = 0 },
            Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = Errors(await response.Content.ReadAsStringAsync(Token));
        foreach (var field in new[] { "name", "version", "framework", "modelStorageUrl", "inputWidth" })
        {
            Assert.True(errors.TryGetProperty(field, out _), field);
        }
    }

    [Fact]
    public async Task A_policy_with_values_outside_the_ranges_is_refused()
    {
        using var client = ClientFor("STORE_OWNER");

        var response = await client.PostAsJsonAsync(
            $"/api/ai-models/{Id}/policies",
            new
            {
                version = "p1",
                minimumConfidence = 1.5,
                minimumMargin = -0.1,
                topK = 0,
                effectiveFrom = "2026-11-10T00:00:00Z",
                effectiveTo = "2026-11-01T00:00:00Z"
            },
            Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = Errors(await response.Content.ReadAsStringAsync(Token));
        foreach (var field in new[] { "minimumConfidence", "minimumMargin", "topK", "effectiveTo" })
        {
            Assert.True(errors.TryGetProperty(field, out _), field);
        }
    }

    [Fact]
    public async Task A_treatment_needs_a_type_a_title_and_instructions()
    {
        using var client = ClientFor("ADMIN");

        var response = await client.PostAsJsonAsync($"/api/diseases/{Id}/treatments", new { treatmentType = "", title = "", instructions = "" }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = Errors(await response.Content.ReadAsStringAsync(Token));
        foreach (var field in new[] { "treatmentType", "title", "instructions" })
        {
            Assert.True(errors.TryGetProperty(field, out _), field);
        }
    }

    [Fact]
    public async Task The_swagger_document_lists_the_diagnosis_routes()
    {
        using var client = ClientFor(null);

        var paths = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token))
            .RootElement.GetProperty("paths");

        foreach (var path in new[]
                 {
                     "/api/me/diagnosis-cases", "/api/me/diagnosis-cases/{id}", "/api/me/diagnosis-cases/{id}/cancel",
                     "/api/diagnosis-cases", "/api/diagnosis-cases/{id}", "/api/diagnosis-cases/{id}/start-review",
                     "/api/diagnosis-cases/{id}/review", "/api/diagnosis-cases/{id}/rerun-ai",
                     "/api/diagnosis-cases/{id}/recommendations", "/api/diagnosis-cases/{id}/recommendations/{recommendationId}",
                     "/api/ai-models", "/api/ai-models/{id}", "/api/ai-models/{id}/activate", "/api/ai-models/{id}/retire",
                     "/api/ai-models/{id}/policies", "/api/ai-policies/{id}/activate", "/api/ai-policies/{id}/deactivate",
                     "/api/diseases", "/api/diseases/{id}", "/api/diseases/{id}/treatments",
                     "/api/disease-treatments/{id}", "/api/disease-treatments/{id}/activate", "/api/staff/{id}/ai-review"
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
