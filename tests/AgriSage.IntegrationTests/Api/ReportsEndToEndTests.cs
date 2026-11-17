using System.Net;
using System.Net.Http.Json;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using static AgriSage.IntegrationTests.Api.EndToEndHost;

namespace AgriSage.IntegrationTests.Api;

// Reports end to end (secondary flow of the demo): known business on one day, then the owner's reports must add up to it.
// A walk-in buys 3 bottles at the counter for cash; a Farmer takes 4 bottles on credit and pays 1 million back through payOS;
// the same Farmer has a confirmed credit order of 2 bottles not yet handed over. Revenue counts only goods handed over
// (7 million, gross profit = sales − cost); the credit filter keeps the 4 million; debt aging shows the 3 million still owed;
// credit exposure = 3 million owed + 2 million reserved; the counter view shows the 3 million cash sale. Without REPORTS.READ
// (not a sale staff default) sale staff see none of them. Every call goes through HTTP with the real services.
public sealed class ReportsEndToEndTests(StaffHttpTests.StaffApiFactory factory)
    : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    [CreditDbFact]
    public async Task The_reports_add_up_to_the_days_business()
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
        var period = $"fromDate={today:yyyy-MM-dd}&toDate={today:yyyy-MM-dd}";

        // A walk-in buys 3 bottles at the counter for cash.
        await OkAsync(await staff.PostAsJsonAsync("/api/counter-sales", new
        {
            customerType = "WALK_IN",
            items = new[] { new { storeProductId = e.StoreProductId, productPackagingId = e.PackagingId, quantity = 3L, lots = new[] { new { inventoryLotId = e.LotId, baseQuantity = 3L } } } },
        }, token), token);

        // The Farmer takes 4 bottles on credit, pays 1 million back, and has 2 more bottles confirmed but not handed over.
        async Task<(Guid OrderId, Guid ItemId)> CreditOrderAsync(long quantity)
        {
            await OkAsync(await farmer.PostAsJsonAsync("/api/me/cart/items",
                new { storeProductId = e.StoreProductId, productPackagingId = e.PackagingId, quantity }, token), token);
            var order = await OkAsync(await farmer.PostAsJsonAsync("/api/me/orders",
                new { source = "FARMER_MOBILE", settlementType = "CREDIT", fulfillmentType = "PICKUP" }, token), token);
            var orderId = order.GetProperty("id").GetGuid();
            var confirmed = await OkAsync(await owner.PostAsync($"/api/orders/{orderId}/confirm", null, token), token);
            return (orderId, confirmed.GetProperty("items")[0].GetProperty("id").GetGuid());
        }
        var (taken, takenItem) = await CreditOrderAsync(4);
        await OkAsync(await owner.PostAsJsonAsync($"/api/orders/{taken}/pickup", new
        {
            items = new[] { new { orderItemId = takenItem, lots = new[] { new { inventoryLotId = e.LotId, baseQuantity = 4L } } } },
        }, token), token);
        var payment = await OkAsync(await farmer.PostAsJsonAsync("/api/me/payments/payos",
            new { paymentContext = "DEBT_REPAYMENT", amount = 1_000_000m }, token), token);
        await OkAsync(await farmer.PostAsync($"/api/payments/{payment.GetProperty("paymentId").GetGuid()}/simulate-paid", null, token), token);
        await CreditOrderAsync(2);

        // Revenue: only what was handed over (3 + 4 bottles); gross profit = sales − cost of goods.
        var summary = (await OkAsync(await owner.GetAsync($"/api/reports/revenue-summary?{period}", token), token)).GetProperty("current");
        Assert.Equal(2, summary.GetProperty("orderCount").GetInt32());
        Assert.Equal(7_000_000m, summary.GetProperty("fulfilledValue").GetDecimal());
        Assert.Equal(7_000_000m, summary.GetProperty("netSales").GetDecimal());
        var cost = summary.GetProperty("costOfGoods").GetDecimal();
        Assert.True(cost > 0);
        Assert.Equal(7_000_000m - cost, summary.GetProperty("grossProfit").GetDecimal());
        var credit = (await OkAsync(await owner.GetAsync($"/api/reports/revenue?{period}&settlementType=CREDIT", token), token)).GetProperty("totals");
        Assert.Equal(4_000_000m, credit.GetProperty("fulfilledValue").GetDecimal());

        // Debt: 4 million − 1 million paid, not yet due. Credit exposure: owed + reserved for the confirmed order.
        var aging = (await OkAsync(await owner.GetAsync("/api/reports/debt-aging", token), token)).GetProperty("totals");
        Assert.Equal(3_000_000m, aging.GetProperty("total").GetDecimal());
        Assert.Equal(3_000_000m, aging.GetProperty("notDue").GetDecimal());
        var exposure = (await OkAsync(await owner.GetAsync("/api/reports/credit-exposure", token), token)).GetProperty("totals");
        Assert.Equal(3_000_000m, exposure.GetProperty("outstanding").GetDecimal());
        Assert.Equal(2_000_000m, exposure.GetProperty("reservedCredit").GetDecimal());
        Assert.Equal(5_000_000m, exposure.GetProperty("exposure").GetDecimal());
        Assert.Equal(15_000_000m, exposure.GetProperty("availableCredit").GetDecimal());

        // The counter view of today: the 3 million cash sale.
        var counter = await OkAsync(await owner.GetAsync("/api/reports/counter-sales-today", token), token);
        Assert.Equal(3_000_000m, counter.GetProperty("paidSalesRevenue").GetDecimal());
        Assert.Equal(1, counter.GetProperty("completedOrderCount").GetInt32());

        // Reports need REPORTS.READ, which sale staff do not have by default (the counter screen shows "—" without it,
        // CounterSalesReportCards.tsx); the owner may delegate it.
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/reports/counter-sales-today", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/reports/revenue-summary?{period}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/reports/debt-aging", token)).StatusCode);
    }
}
