using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static AgriSage.IntegrationTests.Api.EndToEndHost;

namespace AgriSage.IntegrationTests.Api;

// WF-05 end to end (BM04 6.5, FLOW_2 demo script): a credit DELIVERY order of 10 bottles goes in two notes (8 + 2). Trip 1:
// the customer is absent (nothing posted, RETRY_PENDING), the store redispatches, "delivered" without a photo is refused, 6 are
// handed over with proof (PARTIALLY_DELIVERED, debt for the delivered value only), a damaged bottle is reported and resolved
// with a posted stock adjustment, and the last 2 arrive on the next attempt. Trip 2 brings the other 2; the order is
// COMPLETED, the debt equals what was delivered, and another driver never reaches the delivery. Everything through HTTP.
public sealed class MultiTripDeliveryEndToEndTests(StaffHttpTests.StaffApiFactory factory)
    : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    [CreditDbFact]
    public async Task Two_trips_with_a_failed_attempt_a_partial_delivery_and_an_incident_complete_the_order()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await CreditTestDatabase.StartAsync(token);
        await using var host = Start(factory, e);
        await host.GrantDefaultsAsync(RoleCode.Farmer, "FARMER", token);
        var driverId = await host.AddMemberAsync(RoleCode.DeliveryStaff, "DELIVERY_STAFF", "Driver A", token);
        var otherDriverId = await host.AddMemberAsync(RoleCode.DeliveryStaff, "DELIVERY_STAFF", "Driver B", token);
        using var owner = await host.ClientAsync(e.User.UserId!.Value, "STORE_OWNER", token);
        using var farmer = await host.ClientAsync(e.FarmerUserId, "FARMER", token);
        using var driver = await host.ClientAsync(driverId, "DELIVERY_STAFF", token);
        using var otherDriver = await host.ClientAsync(otherDriverId, "DELIVERY_STAFF", token);

        // A confirmed credit DELIVERY order of 10 bottles.
        await OkAsync(await farmer.PostAsJsonAsync("/api/me/cart/items",
            new { storeProductId = e.StoreProductId, productPackagingId = e.PackagingId, quantity = 10L }, token), token);
        var order = await OkAsync(await farmer.PostAsJsonAsync("/api/me/orders", new
        {
            source = "FARMER_MOBILE", settlementType = "CREDIT", fulfillmentType = "DELIVERY",
            deliveryAddress = new { recipientName = "Farmer Test", recipientPhone = "0912345678", addressLine = "Ap 3", province = "Can Tho" },
        }, token), token);
        var orderId = order.GetProperty("id").GetGuid();
        var confirmed = await OkAsync(await owner.PostAsync($"/api/orders/{orderId}/confirm", null, token), token);
        var orderItemId = confirmed.GetProperty("items")[0].GetProperty("id").GetGuid();

        // Two notes (trips): 8 and 2. More than what is left is refused.
        Assert.False((await owner.PostAsJsonAsync("/api/deliveries",
            new { orderId, items = new[] { new { orderItemId, plannedQuantity = 11L } } }, token)).IsSuccessStatusCode);
        var tripOne = await OkAsync(await owner.PostAsJsonAsync("/api/deliveries",
            new { orderId, items = new[] { new { orderItemId, plannedQuantity = 8L } } }, token), token);
        var tripTwo = await OkAsync(await owner.PostAsJsonAsync("/api/deliveries",
            new { orderId, items = new[] { new { orderItemId, plannedQuantity = 2L } } }, token), token);
        var tripOneId = tripOne.GetProperty("id").GetGuid();
        var tripTwoId = tripTwo.GetProperty("id").GetGuid();
        var allocation = tripOne.GetProperty("items")[0].GetProperty("allocations")[0].GetProperty("id").GetGuid();
        Assert.Equal("ASSIGNED", (await OkAsync(await owner.PostAsJsonAsync($"/api/deliveries/{tripOneId}/assign",
            new { assignedToUserId = driverId }, token), token)).GetProperty("status").GetString());
        await OkAsync(await owner.PostAsync($"/api/deliveries/{tripOneId}/dispatch", null, token), token);

        // Another driver never reaches this delivery.
        Assert.False((await otherDriver.GetAsync($"/api/deliveries/{tripOneId}", token)).IsSuccessStatusCode);

        // Attempt 1: the customer is absent. Nothing is posted; the delivery waits for a retry.
        var first = await OkAsync(await driver.PostAsJsonAsync($"/api/deliveries/{tripOneId}/attempts", new { }, token), token);
        var failed = await OkAsync(await driver.PostAsJsonAsync($"/api/deliveries/{tripOneId}/attempts/{Id(first)}/complete",
            new { failureReasonCode = "CUSTOMER_ABSENT", note = "Không có ai ở nhà" }, token), token);
        Assert.Equal("RETRY_PENDING", failed.GetProperty("status").GetString());
        Assert.Equal(0, await DebtEntriesAsync(e, orderId, token));

        // Redispatch; attempt 2 hands over 6 of 8: "delivered" without a photo is refused, with the photo it is posted.
        await OkAsync(await owner.PostAsync($"/api/deliveries/{tripOneId}/dispatch", null, token), token);
        var second = await OkAsync(await driver.PostAsJsonAsync($"/api/deliveries/{tripOneId}/attempts", new { }, token), token);
        var six = new[] { new { allocationId = allocation, deliveredBaseQuantity = 6L } };
        Assert.Equal(HttpStatusCode.BadRequest, (await driver.PostAsJsonAsync(
            $"/api/deliveries/{tripOneId}/attempts/{Id(second)}/complete", new { items = six, receiverName = "Farmer Test" }, token)).StatusCode);
        var proof = await UploadProofAsync(driver, token);
        var partial = await OkAsync(await driver.PostAsJsonAsync($"/api/deliveries/{tripOneId}/attempts/{Id(second)}/complete",
            new { items = six, receiverName = "Farmer Test", proofImageUrl = proof }, token), token);
        Assert.Equal("PARTIALLY_DELIVERED", partial.GetProperty("status").GetString());
        Assert.Equal(1, await DebtEntriesAsync(e, orderId, token));

        // A damaged bottle is reported and resolved with a posted stock adjustment (written off from the lot).
        var incident = await OkAsync(await driver.PostAsJsonAsync($"/api/deliveries/{tripOneId}/incidents", new
        {
            incidentType = "DAMAGED", description = "Bể 1 chai khi bốc xuống", allocationId = allocation, affectedBaseQuantity = 1L,
        }, token), token);
        Assert.Equal("OPEN", incident.GetProperty("status").GetString());
        var adjustment = await OkAsync(await owner.PostAsJsonAsync("/api/inventory/adjustments", new
        {
            reasonCode = "DAMAGED", note = "Chai bể khi giao (sự cố giao hàng)", lines = new[] { new { inventoryLotId = e.LotId, quantityDeltaBase = -1L } },
        }, token), token);
        var resolved = await OkAsync(await owner.PostAsJsonAsync($"/api/deliveries/{tripOneId}/incidents/{Id(incident)}/resolve", new
        {
            resolutionType = "WRITE_OFF", resolutionNote = "Hủy chai bể, giao chai khác", relatedStockMovementId = Id(adjustment),
        }, token), token);
        Assert.Equal("RESOLVED", resolved.GetProperty("status").GetString());

        // Attempt 3 brings the last 2 of trip one; trip two brings the other 2.
        await OkAsync(await owner.PostAsync($"/api/deliveries/{tripOneId}/dispatch", null, token), token);
        var third = await OkAsync(await driver.PostAsJsonAsync($"/api/deliveries/{tripOneId}/attempts", new { }, token), token);
        Assert.Equal("DELIVERED", (await OkAsync(await driver.PostAsJsonAsync($"/api/deliveries/{tripOneId}/attempts/{Id(third)}/complete", new
        {
            items = new[] { new { allocationId = allocation, deliveredBaseQuantity = 2L } }, receiverName = "Farmer Test", proofImageUrl = proof,
        }, token), token)).GetProperty("status").GetString());
        await OkAsync(await owner.PostAsJsonAsync($"/api/deliveries/{tripTwoId}/assign", new { assignedToUserId = driverId }, token), token);
        await OkAsync(await owner.PostAsync($"/api/deliveries/{tripTwoId}/dispatch", null, token), token);
        var last = await OkAsync(await driver.PostAsJsonAsync($"/api/deliveries/{tripTwoId}/attempts", new { }, token), token);
        await OkAsync(await driver.PostAsJsonAsync($"/api/deliveries/{tripTwoId}/attempts/{Id(last)}/complete", new
        {
            items = new[] { new { allocationId = tripTwo.GetProperty("items")[0].GetProperty("allocations")[0].GetProperty("id").GetGuid(), deliveredBaseQuantity = 2L } },
            receiverName = "Farmer Test", proofImageUrl = proof,
        }, token), token);

        // The Farmer follows both deliveries; the order is COMPLETED; debt = delivered value; stock = 1000 − 10 − 1.
        var deliveries = await OkAsync(await farmer.GetAsync($"/api/me/orders/{orderId}/deliveries", token), token);
        Assert.Equal(2, deliveries.GetArrayLength());
        Assert.All(deliveries.EnumerateArray(), d => Assert.Equal("DELIVERED", d.GetProperty("status").GetString()));
        Assert.Equal("COMPLETED", (await OkAsync(await farmer.GetAsync($"/api/me/orders/{orderId}", token), token)).GetProperty("status").GetString());
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        Assert.Equal(10_000_000m, await db.DebtEntries.AsNoTracking().Where(x => x.OrderId == orderId).SumAsync(x => x.OriginalAmount, token));
        var balance = await db.InventoryLotBalances.AsNoTracking().SingleAsync(b => b.InventoryLotId == e.LotId, token);
        Assert.Equal(1000 - 10 - 1, balance.QuantityOnHand);
        Assert.Equal(0, balance.QuantityReserved);
    }

    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static async Task<int> DebtEntriesAsync(CreditTestDatabase e, Guid orderId, CancellationToken token)
    {
        using var s = e.Scope();
        return await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntries.AsNoTracking().CountAsync(x => x.OrderId == orderId, token);
    }

    private static async Task<string> UploadProofAsync(HttpClient client, CancellationToken token)
    {
        using var photo = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(Png);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        photo.Add(bytes, "file", "proof.png");
        return (await OkAsync(await client.PostAsync("/api/files/delivery-proofs", photo, token), token)).GetProperty("url").GetString()!;
    }
}
