using System.Net;
using System.Net.Http.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// F3.19 routes: authentication, roles and request validation, before any database access.
public sealed class CropSeasonClosingHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly string Season = $"/api/crop-seasons/{Guid.NewGuid()}";

    private HttpClient Client(string? role)
    {
        var client = factory.CreateClient();
        if (role != null)
            client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role).Value);
        return client;
    }

    private static HttpRequestMessage Request(string method, string url) => new(new HttpMethod(method), url)
    { Content = method == "POST" ? JsonContent.Create(new { version = 0 }) : null };

    public static TheoryData<string, string> Routes => new()
    {
        { "GET", $"{Season}/closing-check" }, { "POST", $"{Season}/close" }, { "GET", $"{Season}/limit-suggestions" },
        { "GET", "/api/reports/debt-by-season" },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task The_routes_need_store_staff(string method, string url)
    {
        using var anonymous = Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Request(method, url), Token)).StatusCode);
        foreach (var role in new[] { "FARMER", "DELIVERY_STAFF" })
        {
            using var c = Client(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Request(method, url), Token)).StatusCode);
        }
    }

    // Closing, limit suggestions and the season report are for the owner; Sale staff only read the closing check.
    [Theory]
    [InlineData("POST", "/close")]
    [InlineData("GET", "/limit-suggestions")]
    [InlineData("GET", "report")]
    public async Task Sale_staff_cannot_close_or_read_the_owner_views(string method, string path)
    {
        using var c = Client("SALES_STAFF");
        var url = path == "report" ? "/api/reports/debt-by-season" : Season + path;
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Request(method, url), Token)).StatusCode);
    }

    [Theory]
    [InlineData("/api/reports/debt-by-season?year=1999")]
    [InlineData("/api/reports/debt-by-season?pageSize=0")]
    public async Task Invalid_report_filters_are_rejected(string url)
    {
        using var c = Client("ADMIN");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(url, Token)).StatusCode);
    }

    [Fact]
    public async Task A_negative_version_is_rejected()
    {
        using var c = Client("ADMIN");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"{Season}/close", new { version = -1 }, Token)).StatusCode);
    }
}
