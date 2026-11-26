using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common;
using AgriSage.Application.Features.Inventory;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static AgriSage.IntegrationTests.Api.EndToEndHost;

namespace AgriSage.IntegrationTests.Api;

// F4.7 over HTTP with the real services (FLOW_4 "Seasonal product suggestions"): the owner reads the runs the
// reminder prepared; Sale staff need the delegated SEASONAL_RULES.READ; delivery staff and the Farmer have no access.
// There is no endpoint that creates or changes a run.
public sealed class SeasonalSuggestionHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private const string Route = "/api/inventory/seasonal-suggestions";

    [CreditDbFact]
    public async Task Owners_read_sale_staff_need_the_delegation_and_others_have_no_access()
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

        // A run the reminder would have prepared, with one product of the store.
        var today = BusinessCalendar.Today(DateTimeOffset.UtcNow);
        var startsOn = today.AddDays(20);
        Guid runId;
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var productId = await db.StoreProducts.Where(p => p.Id == e.StoreProductId).Select(p => p.ProductId).SingleAsync(token);
            var season = new SeasonalSuggestionSeason("RICE", "Test", startsOn, startsOn.AddDays(60), 14, startsOn.AddYears(-1),
                startsOn.AddYears(-1).AddDays(60));
            var line = new SeasonalSuggestionLine(productId, e.StoreProductId, SeasonalProductGroup.Fertilizer,
                SeasonalSuggestionConfidence.High, "Cùng kỳ năm trước đã bán 300 bao.", "[]", 300, 700, 104, startsOn.AddDays(-21));
            var run = SeasonalSuggestionRun.Create(e.StoreId, "RULE:http", season, null, null, 1, "{}", [line]);
            db.Add(run);
            await db.SaveChangesAsync(token);
            runId = run.Id;
        }

        var listed = await OkAsync(await owner.GetAsync(Route, token), token);
        Assert.Equal(1, listed.GetProperty("totalCount").GetInt64());
        var detail = await OkAsync(await owner.GetAsync($"{Route}/{runId}", token), token);
        var item = Assert.Single(detail.GetProperty("items").EnumerateArray());
        Assert.Equal("FERTILIZER", item.GetProperty("group").GetString());
        Assert.True(item.GetProperty("inStore").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"{Route}/{Guid.NewGuid()}", token)).StatusCode);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("coveringRule").ValueKind);

        // The rule draft fills in the season and its products, with the default factor (no sales last year); saving
        // it is the usual seasonal-rule create, after which the season shows as covered.
        var draft = await OkAsync(await owner.GetAsync($"{Route}/{runId}/rule-draft", token), token);
        Assert.Equal("RICE", draft.GetProperty("cropType").GetString());
        Assert.StartsWith("Vụ Lúa ", draft.GetProperty("name").GetString());
        Assert.Equal(14, draft.GetProperty("prepareDays").GetInt32());
        Assert.Equal(1.5m, draft.GetProperty("demandFactor").GetDecimal());
        Assert.Equal("DEFAULT", draft.GetProperty("demandFactorBasis").GetString());
        var draftLine = Assert.Single(draft.GetProperty("items").EnumerateArray());
        Assert.Equal(e.StoreProductId, draftLine.GetProperty("storeProductId").GetGuid());
        Assert.Equal(104, draftLine.GetProperty("targetStockLevelBase").GetInt64());
        Assert.Empty(draft.GetProperty("skipped").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"{Route}/{Guid.NewGuid()}/rule-draft", token)).StatusCode);
        var created = await owner.PostAsJsonAsync("/api/inventory/seasonal-rules", new
        {
            name = draft.GetProperty("name").GetString(), cropType = draft.GetProperty("cropType").GetString(),
            region = draft.GetProperty("region").GetString(), startsOn = draft.GetProperty("startsOn").GetString(),
            endsOn = draft.GetProperty("endsOn").GetString(), prepareDays = draft.GetProperty("prepareDays").GetInt32(),
            demandFactor = draft.GetProperty("demandFactor").GetDecimal(), isActive = true,
            items = new[] { new { storeProductId = e.StoreProductId, targetStockLevelBase = (long?)104 } },
        }, token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var ruleId = (await created.Content.ReadFromJsonAsync<JsonElement>(token)).GetProperty("id").GetGuid();
        var row = Assert.Single((await OkAsync(await owner.GetAsync(Route, token), token)).GetProperty("items").EnumerateArray());
        Assert.Equal(ruleId, row.GetProperty("coveringRule").GetProperty("id").GetGuid());

        // Nothing creates or changes a run.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await owner.PostAsJsonAsync(Route, new { cropType = "RICE" }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"{Route}/{runId}/decisions", new { }, token)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await driver.GetAsync(Route, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await farmer.GetAsync(Route, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await driver.GetAsync($"{Route}/{runId}/rule-draft", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await farmer.GetAsync($"{Route}/{runId}/rule-draft", token)).StatusCode);

        // Sale staff have no seasonal-rule permission by default; the owner delegates the read.
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(Route, token)).StatusCode);
        var member = await OkAsync(await owner.GetAsync($"/api/staff/{staffId}/permissions", token), token);
        await OkAsync(await owner.PutAsJsonAsync($"/api/staff/{staffId}/permissions", new
        {
            overrides = new[] { new { code = "SEASONAL_RULES.READ", granted = true } },
            version = member.GetProperty("version").GetInt64(), roleVersion = member.GetProperty("roleVersion").GetInt64(),
            reason = "Nhân viên xem gợi ý nhập hàng (E2E)",
        }, token), token);
        Assert.Equal(1, (await OkAsync(await staff.GetAsync(Route, token), token)).GetProperty("totalCount").GetInt64());
        // The read alone does not open the draft: it needs SEASONAL_RULES.MANAGE, like saving the rule.
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"{Route}/{runId}/rule-draft", token)).StatusCode);
    }
}
