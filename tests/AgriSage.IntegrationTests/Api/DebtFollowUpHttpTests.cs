using System.Net;
using System.Net.Http.Json;
using AgriSage.Application.Common;
using AgriSage.Application.Features.Debt;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.Extensions.DependencyInjection;
using static AgriSage.IntegrationTests.Api.EndToEndHost;

namespace AgriSage.IntegrationTests.Api;

// F3.23 over HTTP with the real services: owner and Sale staff log reminders made outside the system (DEBT.FOLLOW_UP, a
// delegable Sale staff default) and read them (DEBT.READ); delivery staff and the Farmer cannot, and the Farmer's own
// debt list never carries the store's notes.
public sealed class DebtFollowUpHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    [CreditDbFact]
    public async Task Owner_and_sale_staff_log_reminders_others_cannot_and_the_Farmer_never_sees_them()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await CreditTestDatabase.StartAsync(token);
        await using var host = Start(factory, e);
        await host.GrantDefaultsAsync(RoleCode.Farmer, "FARMER", token);
        var staffId = await host.AddMemberAsync(RoleCode.SalesStaff, "SALES_STAFF", "Sale Staff", token);
        var driverId = await host.AddMemberAsync(RoleCode.DeliveryStaff, "DELIVERY_STAFF", "Driver", token);
        using var owner = await host.ClientAsync(e.User.UserId!.Value, "STORE_OWNER", token);
        using var staff = await host.ClientAsync(staffId, "SALES_STAFF", token);
        using var driver = await host.ClientAsync(driverId, "DELIVERY_STAFF", token);
        using var farmer = await host.ClientAsync(e.FarmerUserId, "FARMER", token);
        var route = $"/api/customers/{e.FarmerId}/debt/follow-ups";
        var today = BusinessCalendar.Today(DateTimeOffset.UtcNow);

        // Nothing owed yet: nothing to remind (422). Then 2 000 000 owed.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await staff.PostAsJsonAsync(route, new { channel = "PHONE_CALL", outcome = "NO_ANSWER" }, token)).StatusCode);
        using (var s = e.Scope())
            await s.ServiceProvider.GetRequiredService<IDebtService>().ManualAsync(e.FarmerId, new(2_000_000, today.AddDays(10), "Nợ cũ"), token);

        // Sale staff log a visit with a promise; the owner reads it.
        var created = await staff.PostAsJsonAsync(route, new
        {
            channel = "VISIT", outcome = "PROMISED", note = "Ra ruộng gặp, hẹn cuối tuần", promisedDate = today.AddDays(3), promisedAmount = 500_000m,
        }, token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await OkAsync(created, token);
        Assert.Equal("WAITING", body.GetProperty("promiseStatus").GetString());
        Assert.Equal(2_000_000m, body.GetProperty("outstandingAmount").GetDecimal());
        var listed = await OkAsync(await owner.GetAsync(route, token), token);
        var item = Assert.Single(listed.GetProperty("items").EnumerateArray());
        Assert.Equal("SALES_STAFF", item.GetProperty("createdBy").GetProperty("role").GetString());
        Assert.Equal("Ra ruộng gặp, hẹn cuối tuần", item.GetProperty("note").GetString());

        // Invalid input is a 400; delivery staff and the Farmer have no access.
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync(route, new { channel = "FAX", outcome = "NO_ANSWER" }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync(route, new { channel = "PHONE_CALL", outcome = "PROMISED" }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await driver.PostAsJsonAsync(route, new { channel = "PHONE_CALL", outcome = "NO_ANSWER" }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await driver.GetAsync(route, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await farmer.GetAsync(route, token)).StatusCode);

        // Staff lists show the latest reminder; the Farmer's own list never does.
        var staffEntry = Assert.Single((await OkAsync(await staff.GetAsync("/api/debt-entries", token), token)).GetProperty("items").EnumerateArray());
        Assert.Equal("PROMISED", staffEntry.GetProperty("lastFollowUp").GetProperty("outcome").GetString());
        var ownEntry = Assert.Single((await OkAsync(await farmer.GetAsync("/api/me/debt-entries", token), token)).GetProperty("items").EnumerateArray());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, ownEntry.GetProperty("lastFollowUp").ValueKind);

        // The owner takes DEBT.FOLLOW_UP from this staff member: logging is refused, reading still works (DEBT.READ).
        var member = await OkAsync(await owner.GetAsync($"/api/staff/{staffId}/permissions", token), token);
        await OkAsync(await owner.PutAsJsonAsync($"/api/staff/{staffId}/permissions", new
        {
            overrides = new[] { new { code = "DEBT.FOLLOW_UP", granted = false } },
            version = member.GetProperty("version").GetInt64(), roleVersion = member.GetProperty("roleVersion").GetInt64(),
            reason = "Chỉ chủ ghi nhắc nợ (E2E)",
        }, token), token);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(route, new { channel = "PHONE_CALL", outcome = "NO_ANSWER" }, token)).StatusCode);
        await OkAsync(await staff.GetAsync(route, token), token);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync(route, new { channel = "AT_STORE", outcome = "ASKED_MORE_TIME" }, token)).StatusCode);
    }
}
