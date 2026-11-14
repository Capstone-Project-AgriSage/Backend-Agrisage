using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Features.GoodsReceipts.Import;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Spreadsheets;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static AgriSage.IntegrationTests.Api.EndToEndHost;

namespace AgriSage.IntegrationTests.Api;

// WF-04 end to end (BM04 TC-WF04-10): an Excel receipt with an expired row saves nothing; the correct sheet is previewed,
// imported as a DRAFT and confirmed by sale staff, creating the lot (product + lot number + expiry) with STOCK_IN at the
// purchase cost; a stocktake counts 2 bottles fewer in that lot; sale staff cannot complete it, the owner does and the
// difference is posted; the lot's stock card balances (opening + in − out = closing = 98). Every call goes through HTTP.
public sealed class ReceiptToStocktakeEndToEndTests(StaffHttpTests.StaffApiFactory factory)
    : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    [CreditDbFact]
    public async Task An_excel_receipt_creates_a_lot_and_a_stocktake_difference_is_posted_and_balanced()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await CreditTestDatabase.StartAsync(token);
        await using var host = Start(factory, e);
        var staffId = await host.AddMemberAsync(RoleCode.SalesStaff, "SALES_STAFF", "Sale Staff", token);
        using var owner = await host.ClientAsync(e.User.UserId!.Value, "STORE_OWNER", token);
        using var staff = await host.ClientAsync(staffId, "SALES_STAFF", token);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var supplier = await OkAsync(await owner.PostAsJsonAsync("/api/suppliers", new { name = "Đại lý cấp 1 (E2E)", code = "NCC-E2E" }, token), token);
        var supplierId = supplier.GetProperty("id").GetGuid();

        // A sheet with an expired row is refused as a whole and names the row; nothing is saved.
        var refused = await staff.PostAsync("/api/goods-receipts/import",
            Form(supplierId, Xlsx(["SKU", "Bottle", 10, 80_000, "LOT-OLD", today.AddDays(-1)])), token);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        using (var s = e.Scope())
            Assert.False(await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().GoodsReceipts.AnyAsync(token));

        // The correct sheet: preview (nothing saved), import as a DRAFT, confirm.
        var sheet = Xlsx(["SKU", "Bottle", 100, 80_000, "LOT-E2E", new DateOnly(2028, 6, 30)]);
        await OkAsync(await staff.PostAsync("/api/goods-receipts/import/preview", Form(supplierId, sheet), token), token);
        var draft = await OkAsync(await staff.PostAsync("/api/goods-receipts/import", Form(supplierId, sheet), token), token);
        Assert.Equal("DRAFT", draft.GetProperty("status").GetString());
        var confirmed = await OkAsync(await staff.PostAsync($"/api/goods-receipts/{draft.GetProperty("id").GetGuid()}/confirm", null, token), token);
        Assert.Equal("CONFIRMED", confirmed.GetProperty("status").GetString());
        var lotId = confirmed.GetProperty("items")[0].GetProperty("inventoryLotId").GetGuid();
        Assert.NotEqual(e.LotId, lotId);

        // Stocktake: the new lot is counted 2 short (damaged), the old lot as recorded. It starts more than 5 minutes after
        // the receipt: a movement posted within 5 minutes before the snapshot marks the line stale (design §35.19).
        host.Clock.Advance(TimeSpan.FromMinutes(6));
        var stocktake = await OkAsync(await staff.PostAsJsonAsync("/api/stocktakes",
            new { storeProductIds = new[] { e.StoreProductId } }, token), token);
        var stocktakeId = stocktake.GetProperty("id").GetGuid();
        var started = await OkAsync(await staff.PostAsync($"/api/stocktakes/{stocktakeId}/start", null, token), token);
        Assert.Equal("IN_PROGRESS", started.GetProperty("status").GetString());
        var counts = started.GetProperty("items").EnumerateArray().Select(i =>
        {
            var system = i.GetProperty("systemQuantitySnapshot").GetInt64();
            return i.GetProperty("inventoryLotId").GetGuid() == lotId
                ? (object)new { itemId = i.GetProperty("id").GetGuid(), countedQuantity = system - 2, reasonCode = "DAMAGED" }
                : new { itemId = i.GetProperty("id").GetGuid(), countedQuantity = system, reasonCode = (string?)null };
        }).ToArray();
        Assert.Equal(2, counts.Length);
        await OkAsync(await staff.PutAsJsonAsync($"/api/stocktakes/{stocktakeId}/counts", new { counts }, token), token);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync($"/api/stocktakes/{stocktakeId}/complete", null, token)).StatusCode);
        var completed = await OkAsync(await owner.PostAsync($"/api/stocktakes/{stocktakeId}/complete", null, token), token);
        Assert.Equal("COMPLETED", completed.GetProperty("status").GetString());

        // The lot's stock card balances: 0 + 100 in − 2 out = 98.
        var card = await OkAsync(await owner.GetAsync(
            $"/api/inventory/stock-card?storeProductId={e.StoreProductId}&inventoryLotId={lotId}&fromDate={today:yyyy-MM-dd}&toDate={today:yyyy-MM-dd}", token), token);
        var lines = card.GetProperty("lines").EnumerateArray().ToList();
        var inQty = lines.Sum(l => l.GetProperty("inBaseQuantity").GetInt64());
        var outQty = lines.Sum(l => l.GetProperty("outBaseQuantity").GetInt64());
        Assert.Equal(100, inQty);
        Assert.Equal(2, outQty);
        Assert.Equal(card.GetProperty("openingBaseQuantity").GetInt64() + inQty - outQty, card.GetProperty("closingBaseQuantity").GetInt64());
        Assert.Equal(98, card.GetProperty("closingBaseQuantity").GetInt64());
        using (var s = e.Scope())
            Assert.Equal(98, (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().InventoryLotBalances.AsNoTracking()
                .SingleAsync(b => b.InventoryLotId == lotId, token)).QuantityOnHand);
    }

    // A filled receipt sheet in template order (SKU, Packaging, Quantity, UnitCost, LotNumber, ExpiryDate, ManufactureDate).
    private static byte[] Xlsx(params object?[] row)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(ClosedXmlReceiptSpreadsheet.ReceiptSheet);
        for (var column = 0; column < ReceiptSheetColumns.All.Count; column++)
            sheet.Cell(1, column + 1).Value = ReceiptSheetColumns.All[column];
        for (var column = 0; column < row.Length; column++)
            sheet.Cell(2, column + 1).Value = row[column] is DateOnly d ? d.ToDateTime(TimeOnly.MinValue) : XLCellValue.FromObject(row[column]);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static MultipartFormDataContent Form(Guid supplierId, byte[] sheet)
    {
        var form = new MultipartFormDataContent { { new StringContent(supplierId.ToString()), "supplierId" } };
        var file = new ByteArrayContent(sheet);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(file, "file", "nhap-kho.xlsx");
        return form;
    }
}
