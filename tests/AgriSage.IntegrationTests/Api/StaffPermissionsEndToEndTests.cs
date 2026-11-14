using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using static AgriSage.IntegrationTests.Api.EndToEndHost;

namespace AgriSage.IntegrationTests.Api;

// Staff and permissions end to end (secondary flows of the demo): the owner creates a sale staff account; the staff signs in
// with the default sale permissions; the owner takes one away and delegates one manager action, and both changes apply to the
// staff's current session at once; staff cannot change permissions, and the owner cannot delegate a permission that is not
// delegable; a locked account loses its session and cannot sign in until unlocked; a removed staff member cannot sign in.
// Every call goes through HTTP with the real JWT validation, permission evaluator and DI container.
public sealed class StaffPermissionsEndToEndTests(StaffHttpTests.StaffApiFactory factory)
    : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private const string Phone = "0901234567";
    private const string Password = "nhan-vien-2026";

    [CreditDbFact]
    public async Task The_owner_creates_staff_changes_permissions_live_locks_unlocks_and_removes_them()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await CreditTestDatabase.StartAsync(token);
        await using var host = Start(factory, e);
        await host.AddMemberAsync(RoleCode.SalesStaff, "SALES_STAFF", "Sale có sẵn", token); // the role and its defaults exist
        using var owner = await host.ClientAsync(e.User.UserId!.Value, "STORE_OWNER", token);
        using var anonymous = host.App.CreateClient();

        // The owner creates the account; the staff signs in with it.
        var created = await OkAsync(await owner.PostAsJsonAsync("/api/staff",
            new { fullName = "Nhân viên bán hàng", role = "SALES_STAFF", phoneNumber = Phone, email = (string?)null, password = Password }, token), token);
        var staffId = created.GetProperty("id").GetGuid();
        Assert.Equal("ACTIVE", created.GetProperty("status").GetString());
        using var staff = await SignInAsync(host, anonymous, token);

        // Defaults: sale staff read customers; creating a supplier is a manager action.
        object Supplier(string code) => new { name = $"NCC {code}", code };
        await OkAsync(await staff.GetAsync("/api/customers", token), token);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/suppliers", Supplier("NCC-1"), token)).StatusCode);

        // The owner takes CUSTOMERS.READ away and delegates SUPPLIERS.CREATE; the same session follows at once.
        var member = await OkAsync(await owner.GetAsync($"/api/staff/{staffId}/permissions", token), token);
        object Overrides(params (string Code, bool Granted)[] list) => new
        {
            overrides = list.Select(o => new { code = o.Code, granted = o.Granted }).ToArray(),
            version = member.GetProperty("version").GetInt64(), roleVersion = member.GetProperty("roleVersion").GetInt64(),
            reason = "Phân công lại việc (E2E)",
        };
        Assert.False((await owner.PutAsJsonAsync($"/api/staff/{staffId}/permissions", Overrides(("AUDIT.READ", true)), token)).IsSuccessStatusCode);
        member = await OkAsync(await owner.PutAsJsonAsync($"/api/staff/{staffId}/permissions",
            Overrides(("CUSTOMERS.READ", false), ("SUPPLIERS.CREATE", true)), token), token);
        var effective = member.GetProperty("effectivePermissions").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.DoesNotContain("CUSTOMERS.READ", effective);
        Assert.Contains("SUPPLIERS.CREATE", effective);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/customers", token)).StatusCode);
        await OkAsync(await staff.PostAsJsonAsync("/api/suppliers", Supplier("NCC-2"), token), token);
        var mine = (await OkAsync(await staff.GetAsync("/api/me/permissions", token), token)).GetProperty("permissions")
            .EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Contains("SUPPLIERS.CREATE", mine);
        Assert.DoesNotContain("CUSTOMERS.READ", mine);

        // Staff cannot change permissions, not even their own.
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync($"/api/staff/{staffId}/permissions",
            Overrides(("CUSTOMERS.READ", true)), token)).StatusCode);

        // Lock: the current session stops working and signing in is refused; unlock: signing in works again.
        await OkAsync(await owner.PostAsync($"/api/staff/{staffId}/lock", null, token), token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync("/api/me/permissions", token)).StatusCode);
        Assert.False((await anonymous.PostAsJsonAsync("/api/auth/login", new { identifier = Phone, password = Password }, token)).IsSuccessStatusCode);
        await OkAsync(await owner.PostAsync($"/api/staff/{staffId}/unlock", null, token), token);
        using (var again = await SignInAsync(host, anonymous, token))
            await OkAsync(await again.GetAsync("/api/me/permissions", token), token);

        // Remove (semantic delete): the member leaves the store, the account is kept, signing in is refused.
        await OkAsync(await owner.DeleteAsync($"/api/staff/{staffId}", token), token);
        var removed = await OkAsync(await owner.GetAsync($"/api/staff/{staffId}", token), token);
        Assert.NotEqual("ACTIVE", removed.GetProperty("memberStatus").GetString());
        Assert.False((await anonymous.PostAsJsonAsync("/api/auth/login", new { identifier = Phone, password = Password }, token)).IsSuccessStatusCode);
    }

    private static async Task<HttpClient> SignInAsync(EndToEndHost host, HttpClient anonymous, CancellationToken token)
    {
        var login = await OkAsync(await anonymous.PostAsJsonAsync("/api/auth/login", new { identifier = Phone, password = Password }, token), token);
        Assert.Equal("SALES_STAFF", login.GetProperty("user").GetProperty("role").GetString());
        var client = host.App.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        return client;
    }
}
