using System.Net;
using System.Net.Http.Json;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using static AgriSage.IntegrationTests.Api.EndToEndHost;

namespace AgriSage.IntegrationTests.Api;

// WF-02 end to end (BM04 6.2): the owner opens a crop season; a Farmer with a credit profile buys on credit; the store confirms
// (credit reserved, order joined to the season) and hands the goods over; a debt entry arises for the unpaid value, due at the
// season payment date; the Farmer disputes it; sale staff cannot reduce it (owner-only); the owner reduces it with a reason
// code; the Farmer pays the rest through payOS (simulated mode, real status path) and the entry ends PAID with every change in
// the ledger. Every call goes through HTTP with the real JWT validation, permission evaluator and DI container.
public sealed class SeasonalCreditToCollectionEndToEndTests(StaffHttpTests.StaffApiFactory factory)
    : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    [CreditDbFact]
    public async Task A_credit_purchase_becomes_season_debt_that_is_disputed_reduced_by_the_owner_and_paid()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await CreditTestDatabase.StartAsync(token);
        await using var host = Start(factory, e);
        await host.GrantDefaultsAsync(RoleCode.Farmer, "FARMER", token);
        var staffId = await host.AddMemberAsync(RoleCode.SalesStaff, "SALES_STAFF", "Sale Staff", token);
        using var owner = await host.ClientAsync(e.User.UserId!.Value, "STORE_OWNER", token);
        using var farmer = await host.ClientAsync(e.FarmerUserId, "FARMER", token);
        using var staff = await host.ClientAsync(staffId, "SALES_STAFF", token);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

        // The owner opens the season whose order window contains today.
        var season = await OkAsync(await owner.PostAsJsonAsync("/api/crop-seasons", new
        {
            code = "DX-E2E", name = "Đông Xuân (E2E)", orderWindowStart = today.AddDays(-10),
            harvestStart = today.AddDays(60), harvestEnd = today.AddDays(90), graceDays = 15,
        }, token), token);
        var seasonDue = season.GetProperty("paymentDueDate").GetString();

        // The Farmer buys 4 bottles on credit for pickup; the store confirms and hands them over from the lot.
        await OkAsync(await farmer.PostAsJsonAsync("/api/me/cart/items",
            new { storeProductId = e.StoreProductId, productPackagingId = e.PackagingId, quantity = 4L }, token), token);
        var order = await OkAsync(await farmer.PostAsJsonAsync("/api/me/orders",
            new { source = "FARMER_WEB", settlementType = "CREDIT", fulfillmentType = "PICKUP" }, token), token);
        var orderId = order.GetProperty("id").GetGuid();
        Assert.Equal(4_000_000m, order.GetProperty("totalAmount").GetDecimal());
        var confirmed = await OkAsync(await owner.PostAsync($"/api/orders/{orderId}/confirm", null, token), token);
        Assert.Equal("CONFIRMED", confirmed.GetProperty("status").GetString());
        var orderItemId = confirmed.GetProperty("items")[0].GetProperty("id").GetGuid();
        var handed = await OkAsync(await owner.PostAsJsonAsync($"/api/orders/{orderId}/pickup", new
        {
            items = new[] { new { orderItemId, lots = new[] { new { inventoryLotId = e.LotId, baseQuantity = 4L } } } },
        }, token), token);
        Assert.Equal("COMPLETED", handed.GetProperty("status").GetString());

        // One debt entry for the unpaid value, due at the season payment date.
        var entries = await OkAsync(await farmer.GetAsync("/api/me/debt-entries", token), token);
        var entry = Assert.Single(entries.GetProperty("items").EnumerateArray());
        var entryId = entry.GetProperty("id").GetGuid();
        Assert.Equal(4_000_000m, entry.GetProperty("outstandingAmount").GetDecimal());
        Assert.Equal(seasonDue, entry.GetProperty("dueDate").GetString());
        Assert.Equal("OPEN", entry.GetProperty("status").GetString());

        // The Farmer disputes; sale staff may not reduce the debt; the owner reduces it with a reason code.
        var disputed = await OkAsync(await farmer.PostAsJsonAsync($"/api/me/debt-entries/{entryId}/dispute",
            new { reason = "Thiếu 1 chai khi nhận hàng" }, token), token);
        Assert.Equal("DISPUTED", disputed.GetProperty("status").GetString());
        var adjust = new { amount = 1_000_000m, reason = "Thiếu 1 chai khi nhận hàng", reasonCode = "KHAC" };
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync($"/api/debt-entries/{entryId}/adjust", adjust, token)).StatusCode);
        var adjusted = await OkAsync(await owner.PostAsJsonAsync($"/api/debt-entries/{entryId}/adjust", adjust, token), token);
        Assert.Equal(3_000_000m, adjusted.GetProperty("outstandingAmount").GetDecimal());

        // The Farmer pays the rest through payOS; the entry is PAID.
        var payment = await OkAsync(await farmer.PostAsJsonAsync("/api/me/payments/payos",
            new { paymentContext = "DEBT_REPAYMENT", amount = 3_000_000m }, token), token);
        var paid = await OkAsync(await farmer.PostAsync($"/api/payments/{payment.GetProperty("paymentId").GetGuid()}/simulate-paid", null, token), token);
        Assert.Equal("PAID", paid.GetProperty("status").GetString());
        var settled = await OkAsync(await farmer.GetAsync($"/api/me/debt-entries/{entryId}", token), token);
        Assert.Equal("PAID", settled.GetProperty("status").GetString());
        Assert.Equal(0m, settled.GetProperty("outstandingAmount").GetDecimal());

        // Every change is in the ledger: the credit sale, the owner's reduction and the payment.
        var ledger = await OkAsync(await owner.GetAsync($"/api/debt-entries/{entryId}/ledger", token), token);
        var types = ledger.EnumerateArray().Select(t => t.GetProperty("transactionType").GetString()).ToList();
        Assert.Contains("CREDIT_SALE", types);
        Assert.Contains("ADJUSTMENT_OUT", types);
        Assert.Contains("PAYMENT", types);
    }
}
