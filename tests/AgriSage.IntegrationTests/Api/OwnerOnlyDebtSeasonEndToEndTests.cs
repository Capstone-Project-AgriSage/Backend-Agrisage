using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Application.Features.Auth.Services;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Permissions;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Interceptors;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AgriSage.IntegrationTests.Api;

// F3.20, verification V3 of the proposal (F3_7 §10): Sale staff with old, inherited or role-level permissions still cannot go
// beyond the debt policy on the new season routes. Real requests: JWT authentication (real account validator), role and
// permission filters (real evaluator on the database), service, PostgreSQL. Every refusal must leave the data as it was.
public sealed class OwnerOnlyDebtSeasonEndToEndTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // Every season-related code but the dashboard read, which is delegable on purpose and tested on its own.
    private static readonly string[] SeasonCodes =
    [
        "CROP_SEASONS.READ", "CROP_SEASONS.MANAGE", "DEBT_POLICY.READ", "DEBT_POLICY.MANAGE", "DEBT.ROLLOVER",
    ];

    private WebApplicationFactory<Program> HostOver(CreditTestDatabase e) => factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
    {
        // Real behaviour on purpose: no background workers, the real permission evaluator and account validator, and the test's
        // own schema instead of the (unconfigured) application database.
        services.RemoveAll<IHostedService>();
        services.RemoveAll<IPermissionEvaluator>();
        services.AddScoped<IPermissionEvaluator>(sp => sp.GetRequiredService<PermissionEvaluator>());
        services.RemoveAll<IUserAccessValidator>();
        services.AddScoped<IUserAccessValidator, UserAccessValidator>();
        services.RemoveAll<DbContextOptions<AgriSageDbContext>>();
        services.RemoveAll<AgriSageDbContext>();
        services.AddDbContext<AgriSageDbContext>((p, o) => o.UseNpgsql(e.ConnectionString).AddInterceptors(
            p.GetRequiredService<SoftDeleteInterceptor>(), p.GetRequiredService<AuditableEntityInterceptor>(),
            p.GetRequiredService<ConcurrencyVersionInterceptor>()));
    }));

    // The real account validator needs a persisted, usable session whose security version matches the user's.
    private static async Task<HttpClient> AsAsync(WebApplicationFactory<Program> app, CreditTestDatabase e, Guid userId, string role)
    {
        var now = DateTimeOffset.UtcNow;
        Guid sessionId;
        long version;
        using (var s = e.Scope())
        {
            var context = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            version = (await context.Users.AsNoTracking().SingleAsync(u => u.Id == userId, Token)).SecurityVersion;
            var session = new AuthSession(userId, version, now, now.AddHours(1));
            context.AuthSessions.Add(session);
            await context.SaveChangesAsync(Token);
            sessionId = session.Id;
        }

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", app.Services.GetRequiredService<IAccessTokenService>().Issue(userId, role, sessionId, version).Value);
        return client;
    }

    // A Sale member holding the codes by an override row, by the Sale role, or by role default + a null (inherit) override.
    private static async Task<Guid> SaleStaffAsync(CreditTestDatabase e, string style, params string[] codes)
    {
        codes = codes.Length == 0 ? SeasonCodes : codes;
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var existing = await db.Roles.SingleOrDefaultAsync(r => r.Code == RoleCode.SalesStaff, Token);
        var sale = existing ?? new Role(RoleCode.SalesStaff, "Sale");
        var user = new User(sale.Id, "Staff", "hash", $"{Guid.NewGuid():N}@example.test", null);
        var member = new StoreMember(e.StoreId, user.Id);
        if (existing is null) db.Add(sale);
        db.AddRange(user, member);
        await db.SaveChangesAsync(Token);
        foreach (var code in codes)
        {
            var id = PermissionCatalog.ByCode[code].Id;
            if (style == "override") db.StoreMemberPermissions.Add(new StoreMemberPermission(member.Id, id, true));
            else
            {
                db.RolePermissions.Add(new RolePermission(sale.Id, id, true));
                if (style == "inherit") db.StoreMemberPermissions.Add(new StoreMemberPermission(member.Id, id, null));
            }
        }

        await db.SaveChangesAsync(Token);
        return user.Id;
    }

    private static async Task<string> SnapshotAsync(CreditTestDatabase e)
    {
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var entries = (await db.DebtEntries.AsNoTracking().OrderBy(x => x.EntryNumber)
                .Select(x => new { x.EntryNumber, x.OutstandingAmount, x.DueDate, x.CollectionCropSeasonId, x.RolloverCount }).ToListAsync(Token))
            .Select(x => $"{x.EntryNumber}:{x.OutstandingAmount}:{x.DueDate}:{x.CollectionCropSeasonId}:{x.RolloverCount}");
        var seasons = (await db.CropSeasons.AsNoTracking().OrderBy(c => c.Code).Select(c => new { c.Code, c.Version, c.ClosedAt }).ToListAsync(Token))
            .Select(c => $"{c.Code}:{c.Version}:{c.ClosedAt}");
        return string.Join("|", entries) + "/seasons=" + string.Join(",", seasons)
            + $"/policies={await db.StoreDebtPolicies.CountAsync(Token)}/ledger={await db.DebtTransactions.CountAsync(Token)}"
            + $"/actions={await db.DebtEntryActions.CountAsync(Token)}/audits={await db.AuditLogs.CountAsync(Token)}/notices={await db.Notifications.IgnoreQueryFilters().CountAsync(Token)}";
    }

    private static readonly object PolicyBody = new
    {
        defaultGraceDays = 45, reminderOffsets = new { n2 = -7, n4 = 14, n5 = 30 }, staffRolloverLimit = 1, ownerReviewAfterRollovers = 2,
        onTimeSeasonsForLimitRaise = 2, maxCreditTermDays = (int?)null, maxTotalExtensionDays = 365, version = 0,
    };

    [CreditDbFact]
    public async Task Sale_staff_with_every_season_permission_cannot_go_beyond_the_policy()
    {
        foreach (var style in new[] { "override", "role", "inherit" })
        {
            await using var e = await CreditTestDatabase.StartAsync(Token);
            CropSeasonResponse dx;
            using (var s = e.Scope())
                dx = await s.ServiceProvider.GetRequiredService<ICropSeasonService>().CreateAsync(
                    new("DX-2026", "Đông Xuân", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, new DateOnly(2026, 10, 1)), Token);
            var order = await e.FulfillAsync(5, Token, withoutSeason: true);
            Guid entryId;
            using (var s = e.Scope())
                entryId = (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntries.AsNoTracking().SingleAsync(x => x.OrderId == order.Id, Token)).Id;
            var staffId = await SaleStaffAsync(e, style);
            await using var app = HostOver(e);
            var before = await SnapshotAsync(e);

            using (var staff = await AsAsync(app, e, staffId, "SALES_STAFF"))
            {
                // Owner-only by role (Manage) and by permission (not delegable): refused whatever the staff member was granted.
                Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/crop-seasons",
                    new { code = "HT-2027", name = "Hè Thu", harvestStart = "2027-07-15", harvestEnd = "2027-08-15", graceDays = 30 }, Token)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync($"/api/crop-seasons/{dx.Id}",
                    new { name = "Sửa", harvestStart = "2027-02-15", harvestEnd = "2027-03-15", graceDays = 60, version = dx.Version }, Token)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync("/api/debt-policy", PolicyBody, Token)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync($"/api/crop-seasons/{dx.Id}/close", new { version = dx.Version }, Token)).StatusCode);
                // F3.20: starting debt reminders is the owner's, even as a dry run; staff cannot set the start date.
                Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/debt-reminders/initialize", new { dryRun = true }, Token)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/debt-reminders/initialize", new { dryRun = false }, Token)).StatusCode);
                // Without the dashboard-read permission the owner's views are closed to staff.
                Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/crop-seasons/{dx.Id}/limit-suggestions", Token)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/reports/debt-by-season", Token)).StatusCode);
                // Reading is open to staff who hold the read permission.
                Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/crop-seasons", Token)).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/debt-policy", Token)).StatusCode);
            }

            Assert.Equal(before, await SnapshotAsync(e));

            // DEBT.READ_DASHBOARD is delegable (the dashboard works the same way): a member granted only that reads the season
            // report and the limit suggestions, and still cannot write anything.
            var reader = await SaleStaffAsync(e, style, "DEBT.READ_DASHBOARD");
            using (var client = await AsAsync(app, e, reader, "SALES_STAFF"))
            {
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/reports/debt-by-season", Token)).StatusCode);
                // Past the gate: the business rule answers (the season is not closed), not the permission.
                Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync($"/api/crop-seasons/{dx.Id}/limit-suggestions", Token)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/crop-seasons/{dx.Id}/close", new { version = dx.Version }, Token)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/debt-policy", PolicyBody, Token)).StatusCode);
            }

            Assert.Equal(before, await SnapshotAsync(e));

            // Rolling over is delegable but limited: the first one goes through, the second needs the owner. Refused = unchanged.
            using (var staff = await AsAsync(app, e, staffId, "SALES_STAFF"))
            {
                var first = await staff.PostAsJsonAsync($"/api/debt-entries/{entryId}/rollover", new { reasonCode = "MAT_MUA", note = "Mất mùa" }, Token);
                Assert.Equal(HttpStatusCode.OK, first.StatusCode);
                Assert.Equal(1, JsonDocument.Parse(await first.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("rolloverCount").GetInt32());
                var afterFirst = await SnapshotAsync(e);
                Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync($"/api/debt-entries/{entryId}/rollover", new { reasonCode = "MAT_MUA", note = "Lần hai" }, Token)).StatusCode);
                Assert.Equal(afterFirst, await SnapshotAsync(e));
            }
        }
    }

    [CreditDbFact]
    public async Task The_owner_passes_the_gates_on_the_season_routes()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        using (var s = e.Scope())
            await s.ServiceProvider.GetRequiredService<ICropSeasonService>().CreateAsync(
                new("DX-2026", "Đông Xuân", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, new DateOnly(2026, 10, 1)), Token);
        var order = await e.FulfillAsync(5, Token, withoutSeason: true);
        Guid entryId, dxId;
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            entryId = (await db.DebtEntries.AsNoTracking().SingleAsync(x => x.OrderId == order.Id, Token)).Id;
            dxId = (await db.CropSeasons.AsNoTracking().SingleAsync(Token)).Id;
        }

        await using var app = HostOver(e);
        using var owner = await AsAsync(app, e, e.User.UserId!.Value, "STORE_OWNER");
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/crop-seasons",
            new { code = "HT-2027", name = "Hè Thu", harvestStart = "2027-07-15", harvestEnd = "2027-08-15", graceDays = 30 }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/debt-policy", PolicyBody, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/debt-entries/{entryId}/rollover", new { reasonCode = "MAT_MUA", note = "Một" }, Token)).StatusCode);
        var again = await owner.PostAsJsonAsync($"/api/debt-entries/{entryId}/rollover", new { reasonCode = "THIEN_TAI", note = "Hai" }, Token);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(2, JsonDocument.Parse(await again.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("rolloverCount").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/reports/debt-by-season", Token)).StatusCode);
        // F3.20: the dry run (also the default of an empty body) counts and writes nothing; the real run sets the start date once.
        var dry = await owner.PostAsJsonAsync("/api/debt-reminders/initialize", new { }, Token);
        Assert.Equal(HttpStatusCode.OK, dry.StatusCode);
        Assert.True(JsonDocument.Parse(await dry.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("dryRun").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/debt-reminders/initialize", new { dryRun = false }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/crop-seasons/{dxId}/closing-check", Token)).StatusCode);
        // Through the gates, but the harvest has not ended: the business rule refuses, not the permission.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.PostAsJsonAsync($"/api/crop-seasons/{dxId}/close", new { version = 0 }, Token)).StatusCode);
        // The policy saved over HTTP is read back.
        var policy = JsonDocument.Parse(await owner.GetStringAsync("/api/debt-policy", Token)).RootElement;
        Assert.Equal((45, false), (policy.GetProperty("defaultGraceDays").GetInt32(), policy.GetProperty("isDefault").GetBoolean()));
    }

    // A Farmer sees only their own debt and cannot reach any store route; another Farmer's entry is a 404, not a 403.
    [CreditDbFact]
    public async Task A_farmer_reaches_only_their_own_debt_and_no_store_route()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        CropSeasonResponse dx;
        using (var s = e.Scope())
            dx = await s.ServiceProvider.GetRequiredService<ICropSeasonService>().CreateAsync(
                new("DX-2026", "Đông Xuân", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, new DateOnly(2026, 10, 1)), Token);
        var order = await e.OrderAsync(2, Token);
        using (var s = e.Scope()) await s.ServiceProvider.GetRequiredService<AgriSage.Application.Features.Orders.IOrderConfirmationService>().ConfirmAsync(order.Id, Token);
        using (var s = e.Scope()) await s.ServiceProvider.GetRequiredService<AgriSage.Application.Features.Orders.IOrderPickupService>().PickupAsync(order.Id,
            new([new(order.Items.Single().Id, [new(e.LotId, 2)])]), Token);
        Guid own, otherEntry;
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            own = (await db.DebtEntries.AsNoTracking().SingleAsync(x => x.OrderId == order.Id, Token)).Id;
            var role = await db.Roles.SingleAsync(r => r.Code == RoleCode.Farmer, Token);
            var user = new User(role.Id, "Khách khác", "hash", $"{Guid.NewGuid():N}@example.test", null);
            var farmer = new FarmerProfile(user.Id);
            var account = new DebtAccount(e.StoreId, farmer.Id);
            var posting = account.CreateManualAdjustmentEntry("DE-OTHER", 7_000_000m, new DateOnly(2027, 4, 14), e.User.UserId!.Value, e.Clock.UtcNow,
                originCropSeasonId: dx.Id);
            db.AddRange(user, farmer, new FarmerCreditProfile(e.StoreId, farmer.Id, 20_000_000m, e.User.UserId.Value, e.Clock.UtcNow, e.TierId), account,
                posting.Entry, posting.Transaction);
            await db.SaveChangesAsync(Token);
            otherEntry = posting.Entry.Id;
        }

        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var farmerRole = await db.Roles.SingleAsync(r => r.Code == RoleCode.Farmer, Token);
            foreach (var permission in PermissionCatalog.Entries.Where(p => p.DefaultRoles.Contains("FARMER")))
                db.RolePermissions.Add(new RolePermission(farmerRole.Id, permission.Id, true));
            await db.SaveChangesAsync(Token);
        }

        await using var app = HostOver(e);
        using var farmerClient = await AsAsync(app, e, e.FarmerUserId, "FARMER");
        var before = await SnapshotAsync(e);

        // Store routes: refused, including the season and policy routes and the rollover.
        Assert.Equal(HttpStatusCode.Forbidden, (await farmerClient.GetAsync("/api/crop-seasons", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await farmerClient.GetAsync("/api/debt-policy", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await farmerClient.PostAsJsonAsync($"/api/debt-entries/{own}/rollover", new { reasonCode = "MAT_MUA", note = "x" }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await farmerClient.GetAsync($"/api/crop-seasons/{dx.Id}/closing-check", Token)).StatusCode);

        // Own debt: visible with the season fields; someone else's is not found, in the detail and in the filtered list.
        var mine = JsonDocument.Parse(await farmerClient.GetStringAsync($"/api/me/debt-entries/{own}", Token)).RootElement;
        Assert.Equal(("DX-2026", "DX-2026", 2_000_000m), (mine.GetProperty("originSeason").GetProperty("code").GetString(),
            mine.GetProperty("collectionSeason").GetProperty("code").GetString(), mine.GetProperty("outstandingAmount").GetDecimal()));
        Assert.Equal(HttpStatusCode.NotFound, (await farmerClient.GetAsync($"/api/me/debt-entries/{otherEntry}", Token)).StatusCode);
        var list = JsonDocument.Parse(await farmerClient.GetStringAsync($"/api/me/debt-entries?collectionSeasonId={dx.Id}", Token)).RootElement;
        Assert.Equal([own], list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
        var debt = JsonDocument.Parse(await farmerClient.GetStringAsync("/api/me/debt", Token)).RootElement;
        Assert.Equal(2_000_000m, debt.GetProperty("currentBalance").GetDecimal());
        Assert.Equal(2_000_000m, debt.GetProperty("groups").EnumerateArray().Sum(g => g.GetProperty("outstandingAmount").GetDecimal()));
        Assert.Equal(HttpStatusCode.OK, (await farmerClient.GetAsync("/api/me/crop-seasons/current", Token)).StatusCode);
        Assert.Equal(before, await SnapshotAsync(e));
    }
}
