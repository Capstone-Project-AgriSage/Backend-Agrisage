using System.Net;
using System.Net.Http.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// F3.12 routes: authentication, roles and request validation, before any database access.
public sealed class DebtPolicyHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string Route = "/api/debt-policy";

    private HttpClient Client(string? role)
    {
        var client = factory.CreateClient();
        if (role != null)
            client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role).Value);
        return client;
    }

    private static object Body(
        int defaultGraceDays = 30, int n2 = -7, int n4 = 14, int n5 = 30, int staffRolloverLimit = 1,
        int ownerReviewAfterRollovers = 2, int onTimeSeasonsForLimitRaise = 2, int? maxCreditTermDays = null,
        int maxTotalExtensionDays = 365, long version = 0, string? reason = null) => new
    {
        defaultGraceDays,
        reminderOffsets = new { n2, n4, n5 },
        staffRolloverLimit,
        ownerReviewAfterRollovers,
        onTimeSeasonsForLimitRaise,
        maxCreditTermDays,
        maxTotalExtensionDays,
        version,
        reason,
    };

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task The_policy_needs_staff_and_refuses_farmer_and_delivery(string method)
    {
        HttpRequestMessage Request() => new(new HttpMethod(method), Route) { Content = method == "PUT" ? JsonContent.Create(Body()) : null };
        using var anonymous = Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Request(), Token)).StatusCode);
        foreach (var role in new[] { "FARMER", "DELIVERY_STAFF" })
        {
            using var c = Client(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Request(), Token)).StatusCode);
        }
    }

    // The policy decides grace, rollover limits and reminders for every credit customer: Sale staff only read it.
    [Fact]
    public async Task Sale_staff_cannot_change_the_policy()
    {
        using var c = Client("SALES_STAFF");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsJsonAsync(Route, Body(), Token)).StatusCode);
    }

    public static TheoryData<string> InvalidBodies => new()
    {
        "grace-negative", "grace-over-365", "no-offsets", "n2-on-due-date", "n4-before-due-date", "n5-not-after-n4",
        "staff-limit-negative", "owner-review-before-staff-limit", "no-on-time-season", "term-cap-zero",
        "total-extension-zero", "version-negative", "reason-too-long",
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Invalid_values_are_rejected_before_database_access(string change)
    {
        object body = change switch
        {
            "grace-negative" => Body(defaultGraceDays: -1),
            "grace-over-365" => Body(defaultGraceDays: 366),
            "no-offsets" => new { defaultGraceDays = 30, staffRolloverLimit = 1, ownerReviewAfterRollovers = 2,
                onTimeSeasonsForLimitRaise = 2, maxTotalExtensionDays = 365, version = 0 },
            "n2-on-due-date" => Body(n2: 0),
            "n4-before-due-date" => Body(n4: -1),
            "n5-not-after-n4" => Body(n4: 14, n5: 14),
            "staff-limit-negative" => Body(staffRolloverLimit: -1, ownerReviewAfterRollovers: 0),
            "owner-review-before-staff-limit" => Body(staffRolloverLimit: 3, ownerReviewAfterRollovers: 2),
            "no-on-time-season" => Body(onTimeSeasonsForLimitRaise: 0),
            "term-cap-zero" => Body(maxCreditTermDays: 0),
            "total-extension-zero" => Body(maxTotalExtensionDays: 0),
            "version-negative" => Body(version: -1),
            "reason-too-long" => Body(reason: new string('x', 1001)),
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };

        using var c = Client("ADMIN");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync(Route, body, Token)).StatusCode);
    }
}
