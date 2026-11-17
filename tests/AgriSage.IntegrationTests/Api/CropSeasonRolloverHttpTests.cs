using System.Net;
using System.Net.Http.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// F3.15 route: authentication, roles and request validation, before any database access.
public sealed class CropSeasonRolloverHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly string Route = $"/api/crop-seasons/{Guid.NewGuid()}/debt/rollover";

    private HttpClient Client(string? role)
    {
        var client = factory.CreateClient();
        if (role != null)
            client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role).Value);
        return client;
    }

    [Fact]
    public async Task The_season_rollover_needs_store_staff()
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

    public static TheoryData<string> InvalidBodies => new()
    {
        "no-code", "code-not-for-rollover", "no-note", "empty-target", "empty-list", "empty-id", "too-many",
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Invalid_requests_are_rejected(string change)
    {
        object body = change switch
        {
            "no-code" => new { note = "Mất mùa" },
            "code-not-for-rollover" => new { reasonCode = "SAI_SOT", note = "Ghi sai" },
            "no-note" => new { reasonCode = "MAT_MUA" },
            "empty-target" => new { reasonCode = "MAT_MUA", note = "Mất mùa", targetCropSeasonId = Guid.Empty },
            "empty-list" => new { reasonCode = "MAT_MUA", note = "Mất mùa", entryIds = Array.Empty<Guid>() },
            "empty-id" => new { reasonCode = "MAT_MUA", note = "Mất mùa", entryIds = new[] { Guid.Empty } },
            "too-many" => new { reasonCode = "MAT_MUA", note = "Mất mùa", entryIds = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray() },
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };

        using var c = Client("ADMIN");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(Route, body, Token)).StatusCode);
    }
}
