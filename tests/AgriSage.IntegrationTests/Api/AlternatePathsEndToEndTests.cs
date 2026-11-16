using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Notifications;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static AgriSage.IntegrationTests.Api.EndToEndHost;

namespace AgriSage.IntegrationTests.Api;

// The alternate paths of the main workflows, end to end (the questions a jury asks): cancelling orders, a payOS link that
// expires or is cancelled and a webhook that is forged, wrong or repeated, a delivery whose remainder never arrives, a return
// the store rejects, and a debt that becomes overdue. Every call goes through HTTP with the real JWT validation, permission
// evaluator and DI container on an isolated PostgreSQL schema.
public sealed class AlternatePathsEndToEndTests(StaffHttpTests.StaffApiFactory factory)
    : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    // Cancelling: the Farmer may cancel only while the order waits for confirmation; staff cancel a confirmed order with a
    // reason, which gives the stock and the credit reservation back; a paid order that is cancelled asks for a refund, which
    // the owner completes with a reference and a proof photo.
    [CreditDbFact]
    public async Task Cancelling_orders_gives_stock_and_credit_back_and_a_paid_order_is_refunded()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await CreditTestDatabase.StartAsync(token);
        await using var host = Start(factory, e);
        await host.GrantDefaultsAsync(RoleCode.Farmer, "FARMER", token);
        var staffId = await host.AddMemberAsync(RoleCode.SalesStaff, "SALES_STAFF", "Sale Staff", token);
        using var owner = await host.ClientAsync(e.User.UserId!.Value, "STORE_OWNER", token);
        using var farmer = await host.ClientAsync(e.FarmerUserId, "FARMER", token);
        using var staff = await host.ClientAsync(staffId, "SALES_STAFF", token);

        // A confirmed credit order holds 4 bottles and 4 million of credit; the Farmer can no longer cancel it.
        var credit = await PlaceAsync(farmer, e, 4, "CREDIT", token);
        await OkAsync(await owner.PostAsync($"/api/orders/{credit}/confirm", null, token), token);
        Assert.Equal(4, (await BalanceAsync(e, token)).Reserved);
        Assert.Equal(4_000_000m, await ReservedCreditAsync(owner, token));
        Assert.False((await farmer.PostAsJsonAsync($"/api/me/orders/{credit}/cancel", new { reason = "Đổi ý" }, token)).IsSuccessStatusCode);

        // Staff cancel it with a reason (none is refused): stock and credit are given back, no refund is due.
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync($"/api/orders/{credit}/cancel", new { reason = "" }, token)).StatusCode);
        var cancelled = await OkAsync(await staff.PostAsJsonAsync($"/api/orders/{credit}/cancel", new { reason = "Khách đổi ý qua điện thoại" }, token), token);
        Assert.Equal("CANCELLED", cancelled.GetProperty("order").GetProperty("status").GetString());
        Assert.Empty(cancelled.GetProperty("refunds").EnumerateArray());
        Assert.Equal(0, (await BalanceAsync(e, token)).Reserved);
        Assert.Equal(0m, await ReservedCreditAsync(owner, token));

        // A pay-now order paid through payOS, then cancelled by the Farmer while it waits for confirmation: a refund is due.
        var paid = await PlaceAsync(farmer, e, 2, "FULL_PAYMENT", token);
        var payment = await OkAsync(await farmer.PostAsJsonAsync("/api/me/payments/payos", new { paymentContext = "ORDER_PAYMENT", orderId = paid }, token), token);
        await OkAsync(await farmer.PostAsync($"/api/payments/{payment.GetProperty("paymentId").GetGuid()}/simulate-paid", null, token), token);
        Assert.Equal("CANCELLED", (await OkAsync(await farmer.PostAsJsonAsync($"/api/me/orders/{paid}/cancel",
            new { reason = "Đặt nhầm sản phẩm" }, token), token)).GetProperty("status").GetString());
        var refund = Assert.Single((await OkAsync(await owner.GetAsync($"/api/orders/{paid}/refunds", token), token)).EnumerateArray());
        Assert.Equal(2_000_000m, refund.GetProperty("amount").GetDecimal());
        var completed = await OkAsync(await owner.PostAsJsonAsync($"/api/orders/{paid}/refunds/{refund.GetProperty("id").GetGuid()}/complete",
            new { externalReference = "CK-HOAN-0001", proofFileUrl = await UploadAsync(owner, token) }, token), token);
        Assert.Equal("COMPLETED", completed.GetProperty("status").GetString());
    }

    // payOS: an expired link leaves the order unpaid; the Farmer may cancel a link; a forged webhook is refused, a wrong
    // amount is not taken as paid, the right one is applied once however many times payOS repeats it.
    [CreditDbFact]
    public async Task An_expired_or_cancelled_link_leaves_the_order_unpaid_and_only_a_genuine_webhook_pays_it_once()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await CreditTestDatabase.StartAsync(token);
        var payOs = new FakePayOs();
        await using var host = Start(factory, e, services =>
        {
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(payOs);
        });
        await host.GrantDefaultsAsync(RoleCode.Farmer, "FARMER", token);
        using var owner = await host.ClientAsync(e.User.UserId!.Value, "STORE_OWNER", token);
        using var farmer = await host.ClientAsync(e.FarmerUserId, "FARMER", token);
        using var anonymous = host.App.CreateClient();
        var orderId = await PlaceAsync(farmer, e, 2, "FULL_PAYMENT", token);
        async Task<JsonElement> LinkAsync() =>
            await OkAsync(await farmer.PostAsJsonAsync("/api/me/payments/payos", new { paymentContext = "ORDER_PAYMENT", orderId }, token), token);
        async Task<string?> StatusAsync(JsonElement link) =>
            (await OkAsync(await owner.GetAsync($"/api/payments/{link.GetProperty("paymentId").GetGuid()}", token), token)).GetProperty("status").GetString();

        // The first link expires: the payment fails, the order cannot be confirmed.
        var expired = await LinkAsync();
        payOs.Set(expired.GetProperty("providerOrderCode").GetInt64(), PaymentLinkStatus.Expired);
        await OkAsync(await farmer.PostAsync($"/api/payments/{expired.GetProperty("paymentId").GetGuid()}/sync", null, token), token);
        Assert.Equal("FAILED", await StatusAsync(expired));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.PostAsync($"/api/orders/{orderId}/confirm", null, token)).StatusCode);

        // The Farmer cancels the second link.
        var cancelledLink = await LinkAsync();
        await OkAsync(await farmer.PostAsync($"/api/me/payments/{cancelledLink.GetProperty("paymentId").GetGuid()}/cancel", null, token), token);
        Assert.Equal("CANCELLED", await StatusAsync(cancelledLink));

        // The third link: a forged webhook is refused; a wrong amount is not paid; the genuine one pays once, even repeated.
        var link = await LinkAsync();
        var code = link.GetProperty("providerOrderCode").GetInt64();
        Task<HttpResponseMessage> WebhookAsync(string signature, long amount) => anonymous.PostAsync("/api/payments/payos/webhook",
            new StringContent(JsonSerializer.Serialize(new { signature, orderCode = code, amount, paid = true }), Encoding.UTF8, "application/json"), token);
        Assert.Equal(HttpStatusCode.BadRequest, (await WebhookAsync("forged", 2_000_000)).StatusCode);
        Assert.Equal("PENDING", await StatusAsync(link));
        Assert.True((await WebhookAsync("valid", 1_000)).IsSuccessStatusCode);
        Assert.NotEqual("PAID", await StatusAsync(link));
        Assert.True((await WebhookAsync("valid", 2_000_000)).IsSuccessStatusCode);
        Assert.True((await WebhookAsync("valid", 2_000_000)).IsSuccessStatusCode);
        Assert.Equal("PAID", await StatusAsync(link));
        var summary = await OkAsync(await owner.GetAsync($"/api/orders/{orderId}/payments", token), token);
        var paidOnce = summary.GetProperty("payments").EnumerateArray().Where(p => p.GetProperty("status").GetString() == "PAID").ToList();
        Assert.Equal(2_000_000m, Assert.Single(paidOnce).GetProperty("amount").GetDecimal());
        Assert.Equal(2_000_000m, summary.GetProperty("paidAmount").GetDecimal());
        Assert.Equal(0m, summary.GetProperty("remainingToPay").GetDecimal());
        Assert.Equal("CONFIRMED", (await OkAsync(await owner.PostAsync($"/api/orders/{orderId}/confirm", null, token), token)).GetProperty("status").GetString());
    }

    // A delivery whose remainder never arrives: 1 of 3 bottles is handed over, the next attempt fails, the store cancels the
    // delivery note and the rest of the line: the order ends PARTIALLY_CANCELLED with 1 bottle, the debt is for 1 bottle only,
    // nothing stays reserved. The driver cannot cancel the note.
    [CreditDbFact]
    public async Task A_failed_remainder_is_cancelled_and_only_the_delivered_bottle_becomes_debt()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await CreditTestDatabase.StartAsync(token);
        await using var host = Start(factory, e);
        await host.GrantDefaultsAsync(RoleCode.Farmer, "FARMER", token);
        var driverId = await host.AddMemberAsync(RoleCode.DeliveryStaff, "DELIVERY_STAFF", "Driver", token);
        using var owner = await host.ClientAsync(e.User.UserId!.Value, "STORE_OWNER", token);
        using var farmer = await host.ClientAsync(e.FarmerUserId, "FARMER", token);
        using var driver = await host.ClientAsync(driverId, "DELIVERY_STAFF", token);
        await OkAsync(await farmer.PostAsJsonAsync("/api/me/cart/items",
            new { storeProductId = e.StoreProductId, productPackagingId = e.PackagingId, quantity = 3L }, token), token);
        var orderId = (await OkAsync(await farmer.PostAsJsonAsync("/api/me/orders", new
        {
            source = "FARMER_MOBILE", settlementType = "CREDIT", fulfillmentType = "DELIVERY",
            deliveryAddress = new { recipientName = "Farmer Test", recipientPhone = "0912345678", addressLine = "Ap 3", province = "Can Tho" },
        }, token), token)).GetProperty("id").GetGuid();
        var orderItemId = (await OkAsync(await owner.PostAsync($"/api/orders/{orderId}/confirm", null, token), token))
            .GetProperty("items")[0].GetProperty("id").GetGuid();
        var note = await OkAsync(await owner.PostAsJsonAsync("/api/deliveries",
            new { orderId, items = new[] { new { orderItemId, plannedQuantity = 3L } } }, token), token);
        var deliveryId = note.GetProperty("id").GetGuid();
        var allocation = note.GetProperty("items")[0].GetProperty("allocations")[0].GetProperty("id").GetGuid();
        await OkAsync(await owner.PostAsJsonAsync($"/api/deliveries/{deliveryId}/assign", new { assignedToUserId = driverId }, token), token);

        // Attempt 1 hands over 1 bottle; attempt 2 fails.
        await OkAsync(await owner.PostAsync($"/api/deliveries/{deliveryId}/dispatch", null, token), token);
        var first = await OkAsync(await driver.PostAsJsonAsync($"/api/deliveries/{deliveryId}/attempts", new { }, token), token);
        await OkAsync(await driver.PostAsJsonAsync($"/api/deliveries/{deliveryId}/attempts/{first.GetProperty("id").GetGuid()}/complete", new
        {
            items = new[] { new { allocationId = allocation, deliveredBaseQuantity = 1L } }, receiverName = "Farmer Test", proofImageUrl = await UploadAsync(driver, token),
        }, token), token);
        await OkAsync(await owner.PostAsync($"/api/deliveries/{deliveryId}/dispatch", null, token), token);
        var second = await OkAsync(await driver.PostAsJsonAsync($"/api/deliveries/{deliveryId}/attempts", new { }, token), token);
        Assert.Equal("RETRY_PENDING", (await OkAsync(await driver.PostAsJsonAsync($"/api/deliveries/{deliveryId}/attempts/{second.GetProperty("id").GetGuid()}/complete",
            new { failureReasonCode = "CUSTOMER_ABSENT" }, token), token)).GetProperty("status").GetString());

        // The driver cannot cancel; the store cancels the note, then the rest of the line.
        Assert.Equal(HttpStatusCode.Forbidden, (await driver.PostAsJsonAsync($"/api/deliveries/{deliveryId}/cancel", new { reason = "Không giao được" }, token)).StatusCode);
        await OkAsync(await owner.PostAsJsonAsync($"/api/deliveries/{deliveryId}/cancel", new { reason = "Khách không nhận phần còn lại" }, token), token);
        Assert.Equal(2, (await BalanceAsync(e, token)).Reserved); // the order still holds the remainder until it is cancelled
        var order = await OkAsync(await owner.PostAsJsonAsync($"/api/orders/{orderId}/items/{orderItemId}/cancel-remaining",
            new { reason = "Khách không nhận phần còn lại" }, token), token);
        Assert.Equal("PARTIALLY_CANCELLED", order.GetProperty("status").GetString()); // final: part handed over, part cancelled
        var (onHand, reserved) = await BalanceAsync(e, token);
        Assert.Equal(999, onHand);
        Assert.Equal(0, reserved);
        var debt = Assert.Single((await OkAsync(await farmer.GetAsync("/api/me/debt-entries", token), token)).GetProperty("items").EnumerateArray());
        Assert.Equal(1_000_000m, debt.GetProperty("originalAmount").GetDecimal());
        Assert.Equal(0m, await ReservedCreditAsync(owner, token));
    }

    // A rejected return changes nothing: no stock comes back, the debt stays, the bottles can still be returned later. Sale
    // staff may not reject; the Farmer may withdraw a request.
    [CreditDbFact]
    public async Task A_rejected_return_changes_no_stock_or_debt_and_a_request_can_be_withdrawn()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await CreditTestDatabase.StartAsync(token);
        await using var host = Start(factory, e);
        await host.GrantDefaultsAsync(RoleCode.Farmer, "FARMER", token);
        var staffId = await host.AddMemberAsync(RoleCode.SalesStaff, "SALES_STAFF", "Sale Staff", token);
        using var owner = await host.ClientAsync(e.User.UserId!.Value, "STORE_OWNER", token);
        using var farmer = await host.ClientAsync(e.FarmerUserId, "FARMER", token);
        using var staff = await host.ClientAsync(staffId, "SALES_STAFF", token);
        var orderId = await PickedUpCreditOrderAsync(owner, farmer, e, 2, token);
        async Task<JsonElement> RequestAsync()
        {
            var source = (await OkAsync(await farmer.GetAsync($"/api/me/orders/{orderId}/returnable", token), token)).GetProperty("items")[0];
            Assert.Equal(2, source.GetProperty("sources")[0].GetProperty("returnableBaseQuantity").GetInt64());
            return await OkAsync(await farmer.PostAsJsonAsync("/api/me/returns", new
            {
                orderId,
                items = new[]
                {
                    new
                    {
                        orderItemId = source.GetProperty("orderItemId").GetGuid(), returnedBaseQuantity = 1L, reasonCode = "DAMAGED_PRODUCT",
                        originalStockMovementItemId = source.GetProperty("sources")[0].GetProperty("originalStockMovementItemId").GetGuid(),
                    },
                },
                reasonSummary = "Chai bị móp",
            }, token), token);
        }

        var request = await RequestAsync();
        var returnId = request.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync($"/api/returns/{returnId}/reject", new { reason = "Không đủ điều kiện" }, token)).StatusCode);
        var rejected = await OkAsync(await owner.PostAsJsonAsync($"/api/returns/{returnId}/reject",
            new { reason = "Chai móp do vận chuyển của khách, không thuộc diện đổi trả" }, token), token);
        Assert.Equal("REJECTED", rejected.GetProperty("status").GetString());
        Assert.Equal("REJECTED", (await OkAsync(await farmer.GetAsync($"/api/me/returns/{returnId}", token), token)).GetProperty("status").GetString());
        Assert.Equal(998, (await BalanceAsync(e, token)).OnHand);
        var debt = Assert.Single((await OkAsync(await farmer.GetAsync("/api/me/debt-entries", token), token)).GetProperty("items").EnumerateArray());
        Assert.Equal(2_000_000m, debt.GetProperty("outstandingAmount").GetDecimal());

        // Both bottles are still returnable (checked in RequestAsync); the Farmer asks again and withdraws.
        var again = await RequestAsync();
        var withdrawn = await OkAsync(await farmer.PostAsJsonAsync($"/api/me/returns/{again.GetProperty("id").GetGuid()}/cancel",
            new { reason = "Đã tự xử lý" }, token), token);
        Assert.Equal("CANCELLED", withdrawn.GetProperty("status").GetString());
    }

    // A debt becomes overdue: 15 days after the due date the Farmer sees it overdue, the aging report puts it in 1-30 days,
    // the credit check reports it but does not refuse credit by itself (approved policy CS-10: only the owner blocks), and
    // the reminder run sends the +14 day overdue reminder once.
    [CreditDbFact]
    public async Task An_unpaid_debt_becomes_overdue_is_reported_and_reminded_once_without_blocking_credit()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await CreditTestDatabase.StartAsync(token);
        await using var host = Start(factory, e);
        await host.GrantDefaultsAsync(RoleCode.Farmer, "FARMER", token);
        using var owner = await host.ClientAsync(e.User.UserId!.Value, "STORE_OWNER", token);
        using var farmer = await host.ClientAsync(e.FarmerUserId, "FARMER", token);
        await PickedUpCreditOrderAsync(owner, farmer, e, 2, token);
        var entry = Assert.Single((await OkAsync(await farmer.GetAsync("/api/me/debt-entries", token), token)).GetProperty("items").EnumerateArray());
        Assert.False(entry.GetProperty("isOverdue").GetBoolean());
        await OkAsync(await owner.PostAsJsonAsync("/api/debt-reminders/initialize", new { dryRun = false, reason = "Bật nhắc nợ (E2E)" }, token), token);

        // 15 days after the due date.
        var due = DateOnly.Parse(entry.GetProperty("dueDate").GetString()!);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        host.Clock.Advance(TimeSpan.FromDays(due.DayNumber - today.DayNumber + 15));

        entry = Assert.Single((await OkAsync(await farmer.GetAsync("/api/me/debt-entries", token), token)).GetProperty("items").EnumerateArray());
        Assert.True(entry.GetProperty("isOverdue").GetBoolean());
        Assert.Equal(15, entry.GetProperty("overdueDays").GetInt32());
        var aging = (await OkAsync(await owner.GetAsync("/api/reports/debt-aging", token), token)).GetProperty("totals");
        Assert.Equal(2_000_000m, aging.GetProperty("days1To30").GetDecimal());
        var check = await OkAsync(await owner.PostAsJsonAsync($"/api/customers/{e.FarmerId}/credit-eligibility", new { orderAmount = 1_000_000m }, token), token);
        Assert.True(check.GetProperty("hasOverdueDebt").GetBoolean());
        Assert.Equal(2_000_000m, check.GetProperty("overdueAmount").GetDecimal());
        Assert.True(check.GetProperty("eligible").GetBoolean());

        // The reminder run sends the overdue reminder to the Farmer, once.
        async Task<int> OverdueRemindersAsync()
        {
            using (var scope = host.App.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<OperationalAlertsService>().DebtRemindersAsync(100, token);
            return (await OkAsync(await farmer.GetAsync("/api/me/notifications?pageSize=100", token), token)).GetProperty("items")
                .EnumerateArray().Count(n => n.GetProperty("notificationType").GetString() == "DEBT_OVERDUE");
        }
        Assert.Equal(1, await OverdueRemindersAsync());
        Assert.Equal(1, await OverdueRemindersAsync());
    }

    private static async Task<Guid> PlaceAsync(HttpClient farmer, CreditTestDatabase e, long quantity, string settlement, CancellationToken token)
    {
        await OkAsync(await farmer.PostAsJsonAsync("/api/me/cart/items",
            new { storeProductId = e.StoreProductId, productPackagingId = e.PackagingId, quantity }, token), token);
        return (await OkAsync(await farmer.PostAsJsonAsync("/api/me/orders",
            new { source = "FARMER_MOBILE", settlementType = settlement, fulfillmentType = "PICKUP" }, token), token)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> PickedUpCreditOrderAsync(HttpClient owner, HttpClient farmer, CreditTestDatabase e, long quantity, CancellationToken token)
    {
        var orderId = await PlaceAsync(farmer, e, quantity, "CREDIT", token);
        var itemId = (await OkAsync(await owner.PostAsync($"/api/orders/{orderId}/confirm", null, token), token)).GetProperty("items")[0].GetProperty("id").GetGuid();
        await OkAsync(await owner.PostAsJsonAsync($"/api/orders/{orderId}/pickup", new
        {
            items = new[] { new { orderItemId = itemId, lots = new[] { new { inventoryLotId = e.LotId, baseQuantity = quantity } } } },
        }, token), token);
        return orderId;
    }

    private static async Task<(long OnHand, long Reserved)> BalanceAsync(CreditTestDatabase e, CancellationToken token)
    {
        using var s = e.Scope();
        var b = await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().InventoryLotBalances.AsNoTracking()
            .SingleAsync(x => x.InventoryLotId == e.LotId, token);
        return (b.QuantityOnHand, b.QuantityReserved);
    }

    private static async Task<decimal> ReservedCreditAsync(HttpClient owner, CancellationToken token) =>
        (await OkAsync(await owner.GetAsync("/api/reports/credit-exposure", token), token)).GetProperty("totals").GetProperty("reservedCredit").GetDecimal();

    private static async Task<string> UploadAsync(HttpClient client, CancellationToken token)
    {
        using var photo = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(Png);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        photo.Add(bytes, "file", "proof.png");
        return (await OkAsync(await client.PostAsync("/api/files/delivery-proofs", photo, token), token)).GetProperty("url").GetString()!;
    }

    // payOS stand-in: links live in memory; the test sets their state; a webhook body is
    // {"signature":"valid"|..., "orderCode", "amount", "paid"} and only "valid" passes the signature check.
    private sealed class FakePayOs : IPaymentGateway
    {
        private readonly ConcurrentDictionary<long, PaymentLinkState> _links = new();

        public void Set(long orderCode, PaymentLinkStatus status) =>
            _links.AddOrUpdate(orderCode, _ => new(orderCode, status, 0, 0), (_, link) => link with { Status = status });

        public Task<PaymentLinkResult> CreatePaymentLinkAsync(CreatePaymentLinkRequest request, CancellationToken cancellationToken)
        {
            _links[request.OrderCode] = new PaymentLinkState(request.OrderCode, PaymentLinkStatus.Pending, request.Amount, 0);
            return Task.FromResult(new PaymentLinkResult($"fake-{request.OrderCode}", $"https://pay.example.test/{request.OrderCode}", null,
                DateTimeOffset.UtcNow.AddMinutes(15)));
        }

        public Task<PaymentWebhookVerificationResult> VerifyWebhookAsync(string rawPayload, CancellationToken cancellationToken)
        {
            var body = JsonDocument.Parse(rawPayload).RootElement;
            if (body.GetProperty("signature").GetString() != "valid")
                return Task.FromResult(new PaymentWebhookVerificationResult(PaymentWebhookKind.Invalid, null));
            var data = new PaymentWebhookData(body.GetProperty("orderCode").GetInt64(), body.GetProperty("amount").GetInt64(),
                body.GetProperty("paid").GetBoolean(), "FT-E2E", rawPayload);
            return Task.FromResult(new PaymentWebhookVerificationResult(PaymentWebhookKind.Valid, data));
        }

        public Task<PaymentLinkState> GetPaymentLinkAsync(long orderCode, CancellationToken cancellationToken) =>
            Task.FromResult(_links.TryGetValue(orderCode, out var link) ? link : new PaymentLinkState(orderCode, PaymentLinkStatus.Expired, 0, 0));

        public Task<PaymentLinkState> CancelPaymentLinkAsync(long orderCode, string reason, CancellationToken cancellationToken)
        {
            Set(orderCode, PaymentLinkStatus.Cancelled);
            return GetPaymentLinkAsync(orderCode, cancellationToken);
        }
    }
}
