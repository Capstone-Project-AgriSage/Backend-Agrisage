using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static AgriSage.IntegrationTests.Api.EndToEndHost;

namespace AgriSage.IntegrationTests.Api;

// WF-03 end to end (BM04 TC-WF03-10): a Farmer who bought 4 bottles on credit and paid 3.5 of the 4 million returns 2 bottles.
// Too many is refused; sale staff may not approve; the owner approves, staff receive and inspect, the owner completes the
// inspection: the return value first clears the order's unpaid debt (0.5 million) and the rest (1.5 million) is refunded
// outside the system with a reference and a proof photo; the resellable bottles go back into their original lot and the
// return ends COMPLETED. Every call goes through HTTP with the real JWT validation, permission evaluator and DI container.
public sealed class ReturnToRefundEndToEndTests(StaffHttpTests.StaffApiFactory factory)
    : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    [CreditDbFact]
    public async Task A_return_clears_the_unpaid_debt_first_refunds_the_rest_and_restocks_the_original_lot()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await CreditTestDatabase.StartAsync(token);
        await using var host = Start(factory, e);
        await host.GrantDefaultsAsync(RoleCode.Farmer, "FARMER", token);
        var staffId = await host.AddMemberAsync(RoleCode.SalesStaff, "SALES_STAFF", "Sale Staff", token);
        using var owner = await host.ClientAsync(e.User.UserId!.Value, "STORE_OWNER", token);
        using var farmer = await host.ClientAsync(e.FarmerUserId, "FARMER", token);
        using var staff = await host.ClientAsync(staffId, "SALES_STAFF", token);

        // 4 bottles on credit, handed over at the counter, 3.5 million paid through payOS: 0.5 million still owed.
        await OkAsync(await farmer.PostAsJsonAsync("/api/me/cart/items",
            new { storeProductId = e.StoreProductId, productPackagingId = e.PackagingId, quantity = 4L }, token), token);
        var order = await OkAsync(await farmer.PostAsJsonAsync("/api/me/orders",
            new { source = "FARMER_MOBILE", settlementType = "CREDIT", fulfillmentType = "PICKUP" }, token), token);
        var orderId = order.GetProperty("id").GetGuid();
        var confirmed = await OkAsync(await owner.PostAsync($"/api/orders/{orderId}/confirm", null, token), token);
        var orderItemId = confirmed.GetProperty("items")[0].GetProperty("id").GetGuid();
        await OkAsync(await owner.PostAsJsonAsync($"/api/orders/{orderId}/pickup", new
        {
            items = new[] { new { orderItemId, lots = new[] { new { inventoryLotId = e.LotId, baseQuantity = 4L } } } },
        }, token), token);
        var payment = await OkAsync(await farmer.PostAsJsonAsync("/api/me/payments/payos",
            new { paymentContext = "DEBT_REPAYMENT", amount = 3_500_000m }, token), token);
        await OkAsync(await farmer.PostAsync($"/api/payments/{payment.GetProperty("paymentId").GetGuid()}/simulate-paid", null, token), token);
        var entryId = Assert.Single((await OkAsync(await farmer.GetAsync("/api/me/debt-entries", token), token))
            .GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid();

        // The Farmer may return at most what was handed over, from the lot it came from.
        var returnable = await OkAsync(await farmer.GetAsync($"/api/me/orders/{orderId}/returnable", token), token);
        var source = returnable.GetProperty("items")[0].GetProperty("sources")[0];
        Assert.Equal(e.LotId, source.GetProperty("inventoryLotId").GetGuid());
        Assert.Equal(4, source.GetProperty("returnableBaseQuantity").GetInt64());
        object Request(long quantity) => new
        {
            orderId,
            items = new[]
            {
                new
                {
                    orderItemId, returnedBaseQuantity = quantity, reasonCode = "DAMAGED_PRODUCT",
                    originalStockMovementItemId = source.GetProperty("originalStockMovementItemId").GetGuid(),
                },
            },
            reasonSummary = "Chai bị móp",
        };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await farmer.PostAsJsonAsync("/api/me/returns", Request(5), token)).StatusCode);
        var requested = await OkAsync(await farmer.PostAsJsonAsync("/api/me/returns", Request(2), token), token);
        var returnId = requested.GetProperty("id").GetGuid();
        Assert.Equal("REQUESTED", requested.GetProperty("status").GetString());

        // Only the owner approves; staff receive and inspect; the owner completes the inspection.
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync($"/api/returns/{returnId}/approve", null, token)).StatusCode);
        Assert.Equal("APPROVED", (await OkAsync(await owner.PostAsync($"/api/returns/{returnId}/approve", null, token), token))
            .GetProperty("status").GetString());
        var received = await OkAsync(await staff.PostAsync($"/api/returns/{returnId}/receive", null, token), token);
        Assert.Equal("RECEIVED", received.GetProperty("status").GetString());
        var itemId = received.GetProperty("items")[0].GetProperty("id").GetGuid();
        await OkAsync(await staff.PutAsJsonAsync($"/api/returns/{returnId}/items/{itemId}/inspection",
            new { conditionStatus = "RESELLABLE", inspectionNote = "Còn nguyên tem" }, token), token);
        var settled = await OkAsync(await owner.PostAsync($"/api/returns/{returnId}/complete-inspection", null, token), token);

        // Debt first: the 0.5 million still owed is cleared; the remaining 1.5 million is to be refunded.
        Assert.Equal(2_000_000m, settled.GetProperty("totalReturnAmount").GetDecimal());
        Assert.Equal(500_000m, settled.GetProperty("totalDebtAdjustment").GetDecimal());
        Assert.Equal(1_500_000m, settled.GetProperty("totalRefundAmount").GetDecimal());
        Assert.Equal("PARTIALLY_RESOLVED", settled.GetProperty("status").GetString());
        var entry = await OkAsync(await farmer.GetAsync($"/api/me/debt-entries/{entryId}", token), token);
        Assert.Equal(0m, entry.GetProperty("outstandingAmount").GetDecimal());

        // The owner pays back outside the system and records the reference and a proof photo.
        var refund = await OkAsync(await owner.PostAsJsonAsync($"/api/returns/{returnId}/refunds",
            new { refundMethod = "CASH", amount = 1_500_000m }, token), token);
        using var photo = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(Png);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        photo.Add(bytes, "file", "refund.png");
        var uploaded = await OkAsync(await owner.PostAsync("/api/files/delivery-proofs", photo, token), token);
        await OkAsync(await owner.PostAsJsonAsync($"/api/returns/{returnId}/refunds/{refund.GetProperty("id").GetGuid()}/complete",
            new { externalReference = "PC-E2E-0001", proofFileUrl = uploaded.GetProperty("url").GetString() }, token), token);

        // The return is COMPLETED for the Farmer, and the 2 resellable bottles are back in the original lot.
        var final = await OkAsync(await farmer.GetAsync($"/api/me/returns/{returnId}", token), token);
        Assert.Equal("COMPLETED", final.GetProperty("status").GetString());
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        Assert.Equal(1000 - 4 + 2, (await db.InventoryLotBalances.AsNoTracking().SingleAsync(b => b.InventoryLotId == e.LotId, token)).QuantityOnHand);
    }
}
