using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Infrastructure.Persistence;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static AgriSage.IntegrationTests.Api.EndToEndHost;

namespace AgriSage.IntegrationTests.Api;

// Catalog, pricing and counter sale end to end (secondary flows of the demo): the owner adds a product with two sale
// packagings (bottle, box of 10), puts it in the store, receives a lot; it cannot be bought until it is sellable and priced;
// the walk-in list prices it, and a customer group gets its own list. The Farmer of that group sees the group price in the
// cart; sale staff sell a box to a walk-in customer at the counter (walk-in price, lot confirmed by staff, cash PAID, order
// COMPLETED, 10 bottles out of the lot); a price change without a reason and a walk-in credit order are refused.
// Every call goes through HTTP with the real JWT validation, permission evaluator and DI container.
public sealed class CatalogPricingCounterSaleEndToEndTests(StaffHttpTests.StaffApiFactory factory)
    : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    [CreditDbFact]
    public async Task A_new_product_is_priced_per_group_and_sold_at_the_counter_from_its_lot()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await CreditTestDatabase.StartAsync(token);
        await using var host = Start(factory, e);
        await host.GrantDefaultsAsync(RoleCode.Farmer, "FARMER", token);
        var staffId = await host.AddMemberAsync(RoleCode.SalesStaff, "SALES_STAFF", "Sale Staff", token);
        using var owner = await host.ClientAsync(e.User.UserId!.Value, "STORE_OWNER", token);
        using var farmer = await host.ClientAsync(e.FarmerUserId, "FARMER", token);
        using var staff = await host.ClientAsync(staffId, "SALES_STAFF", token);
        Guid bottleUnit, boxUnit;
        using (var s = e.Scope()) // units are reference data (seeded), not created through the API
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            bottleUnit = (await db.Units.SingleAsync(u => u.Code == "BOTTLE", token)).Id;
            var box = new Unit("BOX", "Thùng");
            db.Units.Add(box);
            await db.SaveChangesAsync(token);
            boxUnit = box.Id;
        }

        // The owner adds the product (bottle = base, box of 10) and puts it in the store.
        var category = await OkAsync(await owner.PostAsJsonAsync("/api/categories",
            new { code = "THUOC-BENH-E2E", name = "Thuốc trừ bệnh", description = (string?)null, displayOrder = (int?)null, parentId = (Guid?)null }, token), token);
        var product = await OkAsync(await owner.PostAsJsonAsync("/api/products", new
        {
            sku = "E2E-AMISTAR", name = "Amistar Top 325SC", categoryId = Id(category),
            packagings = new object[]
            {
                new { unitId = bottleUnit, conversionToBase = 1L, isBaseUnit = true, isPurchaseUnit = true, isSaleUnit = true, packagingName = "Chai 250 ml" },
                new { unitId = boxUnit, conversionToBase = 10L, isBaseUnit = false, isPurchaseUnit = true, isSaleUnit = true, packagingName = "Thùng 10 chai" },
            },
        }, token), token);
        var packagings = product.GetProperty("packagings").EnumerateArray().ToList();
        var bottle = Id(packagings.Single(p => p.GetProperty("conversionToBase").GetInt64() == 1));
        var box10 = Id(packagings.Single(p => p.GetProperty("conversionToBase").GetInt64() == 10));
        var storeProductId = Id(await OkAsync(await owner.PostAsJsonAsync("/api/store-products", new { productId = Id(product) }, token), token));

        // A new store product is sellable but has no price yet: it can be put in the cart, where it shows NO_PRICE and has
        // no amount. Marked not sellable, it cannot be added at all.
        object Bottles(long n) => new { storeProductId, productPackagingId = bottle, quantity = n };
        JsonElement Line(JsonElement c) => c.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("storeProductId").GetGuid() == storeProductId);
        var unpriced = Line(await OkAsync(await farmer.PostAsJsonAsync("/api/me/cart/items", Bottles(2), token), token));
        Assert.False(unpriced.GetProperty("isAvailable").GetBoolean());
        Assert.Equal("NO_PRICE", unpriced.GetProperty("unavailableReason").GetString());
        Assert.Equal(JsonValueKind.Null, unpriced.GetProperty("unitPrice").ValueKind);
        await OkAsync(await owner.PostAsync($"/api/store-products/{storeProductId}/mark-not-sellable", null, token), token);
        Assert.False((await farmer.PostAsJsonAsync("/api/me/cart/items", Bottles(1), token)).IsSuccessStatusCode);

        // Receive 5 boxes (50 bottles) into lot LOT-AMI; make it sellable again.
        var supplier = await OkAsync(await owner.PostAsJsonAsync("/api/suppliers", new { name = "Đại lý cấp 1", code = "NCC-AMI" }, token), token);
        var receipt = await OkAsync(await owner.PostAsJsonAsync("/api/goods-receipts", new
        {
            supplierId = Id(supplier),
            items = new[] { new { storeProductId, productPackagingId = box10, receivedQuantity = 5L, purchaseUnitCost = 1_500_000m, supplierLotNumber = "LOT-AMI", expiryDate = new DateOnly(2028, 3, 31) } },
        }, token), token);
        var lotId = (await OkAsync(await owner.PostAsync($"/api/goods-receipts/{Id(receipt)}/confirm", null, token), token))
            .GetProperty("items")[0].GetProperty("inventoryLotId").GetGuid();
        await OkAsync(await owner.PostAsync($"/api/store-products/{storeProductId}/mark-sellable", null, token), token);

        // Walk-in prices on the store's walk-in list; a customer group with its own, lower list.
        var walkIn = (await OkAsync(await owner.GetAsync("/api/price-lists?isWalkInDefault=true", token), token)).GetProperty("items")[0];
        await OkAsync(await owner.PutAsJsonAsync($"/api/price-lists/{Id(walkIn)}/items", new
        {
            items = new[] { new { storeProductId, productPackagingId = bottle, sellingPrice = 200_000m }, new { storeProductId, productPackagingId = box10, sellingPrice = 1_900_000m } },
        }, token), token);
        var group = await OkAsync(await owner.PostAsJsonAsync("/api/customer-groups", new { code = "THAN-THIET", name = "Khách thân thiết" }, token), token);
        var groupList = await OkAsync(await owner.PostAsJsonAsync("/api/price-lists",
            new { code = "GIA-THAN-THIET", name = "Giá khách thân thiết", effectiveFrom = DateTimeOffset.UtcNow.AddHours(-1) }, token), token);
        await OkAsync(await owner.PutAsJsonAsync($"/api/price-lists/{Id(groupList)}/items", new
        {
            items = new[] { new { storeProductId, productPackagingId = bottle, sellingPrice = 180_000m }, new { storeProductId, productPackagingId = box10, sellingPrice = 1_700_000m } },
        }, token), token);
        await OkAsync(await owner.PostAsync($"/api/price-lists/{Id(groupList)}/activate", null, token), token);
        await OkAsync(await owner.PutAsJsonAsync($"/api/customer-groups/{Id(group)}/price-list", new { priceListId = Id(groupList) }, token), token);
        await OkAsync(await owner.PutAsJsonAsync($"/api/customers/{e.FarmerId}/group",
            new { customerGroupId = Id(group), reason = "Mua đều mỗi vụ" }, token), token);

        // The same cart line now carries the group price (cart prices are recalculated on every read).
        var line = Line(await OkAsync(await farmer.GetAsync("/api/me/cart", token), token));
        Assert.True(line.GetProperty("isAvailable").GetBoolean());
        Assert.Equal(180_000m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(360_000m, line.GetProperty("lineTotalAmount").GetDecimal());

        // Counter sale to a walk-in customer: walk-in price; FEFO proposes the lot; staff confirm it; cash; COMPLETED.
        object Box(decimal? price = null, string? reason = null, long lotQuantity = 0) => new
        {
            storeProductId, productPackagingId = box10, quantity = 1L, unitPrice = price, overrideReason = reason,
            lots = lotQuantity == 0 ? null : new[] { new { inventoryLotId = lotId, baseQuantity = lotQuantity } },
        };
        var preview = await OkAsync(await staff.PostAsJsonAsync("/api/counter-sales/preview", new { customerType = "WALK_IN", items = new[] { Box() } }, token), token);
        var item = preview.GetProperty("items")[0];
        Assert.Equal(1_900_000m, item.GetProperty("unitPrice").GetDecimal());
        var suggested = item.GetProperty("lots")[0];
        Assert.Equal(lotId, suggested.GetProperty("inventoryLotId").GetGuid());
        Assert.Equal(10, suggested.GetProperty("suggestedBaseQuantity").GetInt64());
        Assert.False((await staff.PostAsJsonAsync("/api/counter-sales",
            new { customerType = "WALK_IN", items = new[] { Box(1_800_000m, null, 10) } }, token)).IsSuccessStatusCode);
        var sale = await OkAsync(await staff.PostAsJsonAsync("/api/counter-sales",
            new { customerType = "WALK_IN", items = new[] { Box(lotQuantity: 10) }, customerName = "Khách lẻ" }, token), token);
        Assert.Equal("COMPLETED", sale.GetProperty("order").GetProperty("status").GetString());
        Assert.Equal(1_900_000m, sale.GetProperty("order").GetProperty("totalAmount").GetDecimal());
        Assert.Equal("PAID", sale.GetProperty("payment").GetProperty("status").GetString());
        Assert.Equal(1_900_000m, sale.GetProperty("payment").GetProperty("amount").GetDecimal());

        // A walk-in customer cannot buy on credit.
        Assert.False((await staff.PostAsJsonAsync("/api/orders", new
        {
            customerType = "WALK_IN", settlementType = "CREDIT", fulfillmentType = "PICKUP",
            items = new[] { new { storeProductId, productPackagingId = bottle, quantity = 1L } },
        }, token)).IsSuccessStatusCode);

        // 50 received − 10 sold; nothing left reserved.
        using var scope = e.Scope();
        var balance = await scope.ServiceProvider.GetRequiredService<AgriSageDbContext>().InventoryLotBalances.AsNoTracking()
            .SingleAsync(b => b.InventoryLotId == lotId, token);
        Assert.Equal(40, balance.QuantityOnHand);
        Assert.Equal(0, balance.QuantityReserved);
    }

    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();
}
