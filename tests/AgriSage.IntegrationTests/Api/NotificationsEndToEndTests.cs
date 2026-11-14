using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Features.Notifications;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.Extensions.DependencyInjection;
using static AgriSage.IntegrationTests.Api.EndToEndHost;

namespace AgriSage.IntegrationTests.Api;

// Notifications end to end (secondary flow of the demo): a Farmer places a credit pickup order; the store team (owner and sale
// staff) is told there is a new order, the Farmer is not; the store confirms and hands the goods over, and the Farmer is told
// the order was confirmed, picked up and that a debt arose. Notifications come from the audit outbox: the test runs the same
// dispatcher the background worker runs, twice, and nothing is duplicated. The Farmer reads one, then all, and archives one;
// nobody can touch another person's notification. Every call goes through HTTP with the real services.
public sealed class NotificationsEndToEndTests(StaffHttpTests.StaffApiFactory factory)
    : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    [CreditDbFact]
    public async Task Order_events_reach_the_right_people_once_and_the_farmer_reads_and_archives_them()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await CreditTestDatabase.StartAsync(token);
        await using var host = Start(factory, e);
        await host.GrantDefaultsAsync(RoleCode.Farmer, "FARMER", token);
        var staffId = await host.AddMemberAsync(RoleCode.SalesStaff, "SALES_STAFF", "Sale Staff", token);
        using var owner = await host.ClientAsync(e.User.UserId!.Value, "STORE_OWNER", token);
        using var farmer = await host.ClientAsync(e.FarmerUserId, "FARMER", token);
        using var staff = await host.ClientAsync(staffId, "SALES_STAFF", token);

        // The Farmer places a credit pickup order: the store team is told, the Farmer is not.
        await OkAsync(await farmer.PostAsJsonAsync("/api/me/cart/items",
            new { storeProductId = e.StoreProductId, productPackagingId = e.PackagingId, quantity = 2L }, token), token);
        var order = await OkAsync(await farmer.PostAsJsonAsync("/api/me/orders",
            new { source = "FARMER_MOBILE", settlementType = "CREDIT", fulfillmentType = "PICKUP" }, token), token);
        var orderId = order.GetProperty("id").GetGuid();
        Assert.True(await DispatchAsync(host, token) > 0);
        Assert.Contains("ORDER_PLACED", await TypesAsync(owner, token));
        Assert.Contains("ORDER_PLACED", await TypesAsync(staff, token));
        Assert.Empty(await TypesAsync(farmer, token));

        // The store confirms and hands the goods over: the Farmer is told about the order and the new debt.
        var confirmed = await OkAsync(await owner.PostAsync($"/api/orders/{orderId}/confirm", null, token), token);
        var orderItemId = confirmed.GetProperty("items")[0].GetProperty("id").GetGuid();
        await OkAsync(await owner.PostAsJsonAsync($"/api/orders/{orderId}/pickup", new
        {
            items = new[] { new { orderItemId, lots = new[] { new { inventoryLotId = e.LotId, baseQuantity = 2L } } } },
        }, token), token);
        await DispatchAsync(host, token);
        var farmerTypes = await TypesAsync(farmer, token);
        Assert.Contains("ORDER_STATUS_CHANGED", farmerTypes);
        Assert.Contains("DEBT_CREATED", farmerTypes);
        Assert.DoesNotContain("ORDER_PLACED", farmerTypes);
        var unread = await UnreadAsync(farmer, token);
        Assert.Equal(farmerTypes.Count, unread);

        // Running the dispatcher again adds nothing (each event is delivered once).
        Assert.Equal(0, await DispatchAsync(host, token));
        Assert.Equal(farmerTypes.Count, (await TypesAsync(farmer, token)).Count);

        // Nobody touches another person's notification; the Farmer reads one, then all, and archives one.
        var items = (await OkAsync(await farmer.GetAsync("/api/me/notifications", token), token)).GetProperty("items").EnumerateArray().ToList();
        var first = items[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await staff.PostAsync($"/api/me/notifications/{first}/read", null, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await farmer.PostAsync($"/api/me/notifications/{first}/read", null, token)).StatusCode);
        Assert.Equal(unread - 1, await UnreadAsync(farmer, token));
        Assert.Equal(HttpStatusCode.NoContent, (await farmer.PostAsync("/api/me/notifications/read-all", null, token)).StatusCode);
        Assert.Equal(0, await UnreadAsync(farmer, token));
        Assert.Equal(HttpStatusCode.NoContent, (await farmer.DeleteAsync($"/api/me/notifications/{first}", token)).StatusCode);
        Assert.Equal(items.Count - 1, (await TypesAsync(farmer, token)).Count);
    }

    // What the background worker does: take the due outbox events and dispatch each in its own scope.
    private static async Task<int> DispatchAsync(EndToEndHost host, CancellationToken token)
    {
        List<Guid> due;
        using (var scope = host.App.Services.CreateScope())
            due = await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().DueAsync(500, token);
        foreach (var id in due)
        {
            using var scope = host.App.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().DispatchAsync(id, token);
        }
        return due.Count;
    }

    private static async Task<List<string>> TypesAsync(HttpClient client, CancellationToken token) =>
        (await OkAsync(await client.GetAsync("/api/me/notifications?pageSize=100", token), token)).GetProperty("items")
        .EnumerateArray().Select(n => n.GetProperty("notificationType").GetString()!).ToList();

    private static async Task<long> UnreadAsync(HttpClient client, CancellationToken token) =>
        (await OkAsync(await client.GetAsync("/api/me/notifications/unread-count", token), token)).GetProperty("count").GetInt64();
}
