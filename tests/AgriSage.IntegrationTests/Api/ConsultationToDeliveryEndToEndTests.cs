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

// WF-01 end to end (BM04 TC-WF01-19): the store approves a registered product for a problem; a Farmer finds the problem
// typed without accents, gets advice with the quantity for the field, buys it, pays through payOS (simulated mode, the
// real status path), the store confirms with FEFO, a delivery staff member delivers with a proof photo, and the order is
// COMPLETED with the stock taken from the lot and the advice marked as bought. Every call goes through HTTP.
public sealed class ConsultationToDeliveryEndToEndTests(StaffHttpTests.StaffApiFactory factory)
    : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    [CreditDbFact]
    public async Task Advice_for_a_problem_ends_in_a_paid_delivered_and_completed_order()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await CreditTestDatabase.StartAsync(token);
        await using var host = Start(factory, e);
        await host.GrantDefaultsAsync(RoleCode.Farmer, "FARMER", token);
        var driverId = await host.AddMemberAsync(RoleCode.DeliveryStaff, "DELIVERY_STAFF", "Driver", token);
        Guid productId;
        using (var s = e.Scope())
            productId = (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().StoreProducts.AsNoTracking()
                .SingleAsync(x => x.Id == e.StoreProductId, token)).ProductId;
        using var owner = await host.ClientAsync(e.User.UserId!.Value, "STORE_OWNER", token);
        using var farmer = await host.ClientAsync(e.FarmerUserId, "FARMER", token);
        using var driver = await host.ClientAsync(driverId, "DELIVERY_STAFF", token);

        // The store records the problem and links a registered product, then approves the link.
        var problem = await OkAsync(await owner.PostAsJsonAsync("/api/diseases", new
        {
            code = "E2E_LEAF_BLAST", name = "Đạo ôn lá (E2E)", problemType = "DISEASE", searchKeywords = "chay la",
        }, token), token);
        var problemId = problem.GetProperty("id").GetGuid();
        var link = await OkAsync(await owner.PostAsJsonAsync($"/api/diseases/{problemId}/products", new
        {
            productId, recommendationGroup = "PROTECTION", doseText = "2 chai/ha", registrationSource = "TT 75/2025 Phụ lục I",
            dosePerHaBase = 2m, applications = 1, preHarvestDays = 14,
        }, token), token);
        await OkAsync(await owner.PostAsync($"/api/disease-products/{link.GetProperty("id").GetGuid()}/approve", null, token), token);

        // The Farmer types the problem without accents and asks for advice for 1.5 ha: 2 bottles/ha → 3 bottles.
        var problems = await OkAsync(await farmer.GetAsync("/api/me/advisory/problems?search=dao%20on", token), token);
        Assert.Contains(problems.GetProperty("items").EnumerateArray(), p => p.GetProperty("id").GetGuid() == problemId);
        var advice = await OkAsync(await farmer.PostAsJsonAsync("/api/me/advisory/recommendations",
            new { diseaseId = problemId, areaSqm = 15_000m }, token), token);
        var advised = Assert.Single(advice.GetProperty("protection").EnumerateArray());
        Assert.Equal(e.StoreProductId, advised.GetProperty("storeProductId").GetGuid());
        Assert.Equal("ENOUGH", advised.GetProperty("stockStatus").GetString());
        Assert.Equal("TT 75/2025 Phụ lục I", advised.GetProperty("registrationSource").GetString());
        var plan = advised.GetProperty("plan");
        Assert.Equal(3, plan.GetProperty("baseQuantity").GetInt64());
        Assert.Equal(3_000_000m, plan.GetProperty("totalAmount").GetDecimal());

        // The advice goes into the cart and becomes a delivery order paid in full.
        foreach (var line in plan.GetProperty("lines").EnumerateArray())
            await OkAsync(await farmer.PostAsJsonAsync("/api/me/cart/items", new
            {
                storeProductId = e.StoreProductId,
                productPackagingId = line.GetProperty("productPackagingId").GetGuid(),
                quantity = line.GetProperty("quantity").GetInt64(),
            }, token), token);
        var order = await OkAsync(await farmer.PostAsJsonAsync("/api/me/orders", new
        {
            source = "FARMER_MOBILE", settlementType = "FULL_PAYMENT", fulfillmentType = "DELIVERY",
            deliveryAddress = new { recipientName = "Farmer Test", recipientPhone = "0912345678", addressLine = "Ap 3", province = "Can Tho" },
        }, token), token);
        var orderId = order.GetProperty("id").GetGuid();
        Assert.Equal("PENDING_CONFIRMATION", order.GetProperty("status").GetString());
        Assert.Equal(3_000_000m, order.GetProperty("totalAmount").GetDecimal());

        // A pay-now order cannot be confirmed before it is paid.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.PostAsync($"/api/orders/{orderId}/confirm", null, token)).StatusCode);

        // payOS: the link is created, the payment is applied once through the status path, the order is covered.
        var payment = await OkAsync(await farmer.PostAsJsonAsync("/api/me/payments/payos",
            new { paymentContext = "ORDER_PAYMENT", orderId }, token), token);
        var paid = await OkAsync(await farmer.PostAsync($"/api/payments/{payment.GetProperty("paymentId").GetGuid()}/simulate-paid", null, token), token);
        Assert.Equal("PAID", paid.GetProperty("status").GetString());
        Assert.Equal(3_000_000m, paid.GetProperty("amount").GetDecimal());

        // The store confirms: stock is reserved FEFO.
        var confirmed = await OkAsync(await owner.PostAsync($"/api/orders/{orderId}/confirm", null, token), token);
        Assert.Equal("CONFIRMED", confirmed.GetProperty("status").GetString());
        var orderItemId = confirmed.GetProperty("items")[0].GetProperty("id").GetGuid();

        // Delivery note, driver, dispatch, attempt with a proof photo.
        var delivery = await OkAsync(await owner.PostAsJsonAsync("/api/deliveries",
            new { orderId, items = new[] { new { orderItemId, plannedQuantity = 3L } } }, token), token);
        var deliveryId = delivery.GetProperty("id").GetGuid();
        var allocationId = delivery.GetProperty("items")[0].GetProperty("allocations")[0].GetProperty("id").GetGuid();
        await OkAsync(await owner.PostAsJsonAsync($"/api/deliveries/{deliveryId}/assign", new { assignedToUserId = driverId }, token), token);
        await OkAsync(await owner.PostAsync($"/api/deliveries/{deliveryId}/dispatch", null, token), token);
        var attempt = await OkAsync(await driver.PostAsJsonAsync($"/api/deliveries/{deliveryId}/attempts", new { }, token), token);
        using var photo = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(Png);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        photo.Add(bytes, "file", "proof.png");
        var uploaded = await OkAsync(await driver.PostAsync("/api/files/delivery-proofs", photo, token), token);
        var completed = await OkAsync(await driver.PostAsJsonAsync(
            $"/api/deliveries/{deliveryId}/attempts/{attempt.GetProperty("id").GetGuid()}/complete", new
            {
                items = new[] { new { allocationId, deliveredBaseQuantity = 3L } },
                receiverName = "Farmer Test",
                proofImageUrl = uploaded.GetProperty("url").GetString(),
            }, token), token);
        Assert.Equal("DELIVERED", completed.GetProperty("status").GetString());

        // The Farmer sees the order completed, the stock left the lot, and the store sees the advice was bought.
        var final = await OkAsync(await farmer.GetAsync($"/api/me/orders/{orderId}", token), token);
        Assert.Equal("COMPLETED", final.GetProperty("status").GetString());
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var balance = await db.InventoryLotBalances.AsNoTracking().SingleAsync(b => b.InventoryLotId == e.LotId, token);
            Assert.Equal(1000 - 3, balance.QuantityOnHand);
            Assert.Equal(0, balance.QuantityReserved);
        }
        var sessions = await OkAsync(await owner.GetAsync("/api/advisory/sessions", token), token);
        var session = Assert.Single(sessions.GetProperty("items").EnumerateArray(),
            x => x.GetProperty("id").GetGuid() == advice.GetProperty("sessionId").GetGuid());
        Assert.True(session.GetProperty("boughtWithin7Days").GetBoolean());
    }
}
