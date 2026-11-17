using System.Net;
using System.Net.Http.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// F3.14 routes: authentication, roles and request validation, before any database access.
public sealed class DebtRolloverHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly string Route = $"/api/debt-entries/{Guid.NewGuid()}/rollover";

    private HttpClient Client(string? role)
    {
        var client = factory.CreateClient();
        if (role != null)
            client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role).Value);
        return client;
    }

    [Fact]
    public async Task Rollover_needs_store_staff()
    {
        var body = new { reasonCode = "MAT_MUA", note = "Mất mùa" };
        using var anonymous = Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(Route, body, Token)).StatusCode);
        foreach (var role in new[] { "FARMER", "DELIVERY_STAFF" })
        {
            using var c = Client(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync(Route, body, Token)).StatusCode);
        }
    }

    public static TheoryData<string> InvalidRollovers => new()
    {
        "no-code", "unknown-code", "code-not-for-rollover", "no-note", "blank-note", "note-too-long", "empty-target",
    };

    [Theory]
    [MemberData(nameof(InvalidRollovers))]
    public async Task Rollover_needs_a_hardship_code_and_a_note(string change)
    {
        object body = change switch
        {
            "no-code" => new { note = "Mất mùa" },
            "unknown-code" => new { reasonCode = "BOGUS", note = "Mất mùa" },
            "code-not-for-rollover" => new { reasonCode = "TRI_AN", note = "Khách quen" },
            "no-note" => new { reasonCode = "MAT_MUA" },
            "blank-note" => new { reasonCode = "MAT_MUA", note = "  " },
            "note-too-long" => new { reasonCode = "MAT_MUA", note = new string('x', 1001) },
            "empty-target" => new { reasonCode = "MAT_MUA", note = "Mất mùa", targetCropSeasonId = Guid.Empty },
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };

        using var c = Client("ADMIN");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(Route, body, Token)).StatusCode);
    }

    // The code is optional on these routes until D7 makes it mandatory, but a code that is sent must be a known one.
    [Theory]
    [InlineData("adjust", "{\"amount\":1000,\"reason\":\"Giảm\",\"reasonCode\":\"BOGUS\"}")]
    [InlineData("cancel", "{\"reason\":\"Xóa\",\"reasonCode\":\"BOGUS\"}")]
    [InlineData("change-due-date", "{\"newDueDate\":\"2027-01-01\",\"reason\":\"Đổi\",\"reasonCode\":\"BOGUS\"}")]
    public async Task Owner_debt_actions_reject_an_unknown_reason_code(string action, string json)
    {
        using var c = Client("ADMIN");
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await c.PostAsync($"/api/debt-entries/{Guid.NewGuid()}/{action}", content, Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
