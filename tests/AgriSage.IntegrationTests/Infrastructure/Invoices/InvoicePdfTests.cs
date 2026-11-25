using System.Text;
using System.Text.Json;
using AgriSage.Application.Features.Invoices;
using AgriSage.Application.Features.Orders;
using AgriSage.Infrastructure.Invoices;

namespace AgriSage.IntegrationTests.Infrastructure.Invoices;

public class InvoicePdfTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Renderer_generates_Vietnamese_documents_and_paginates_long_tables(int lines)
    {
        var now = DateTimeOffset.Parse("2026-11-23T03:15:00Z");
        var order = JsonSerializer.Deserialize<OrderResponse>(JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid(), orderNumber = "OD-20261123-0001", customerName = "Nguyễn Thị Hồng - Đặng Văn Đạt", customerPhone = "0901234567",
            createdAt = now, completedAt = now, settlementType = "FULL_PAYMENT", totalAmount = lines * 45000m,
            note = "Ghi chú tiếng Việt: Bảo quản nơi khô ráo, tránh ánh nắng trực tiếp.",
            items = Enumerable.Range(1, lines).Select(i => new { id = Guid.NewGuid(), sku = "SP-" + i, productName = "Phân bón hữu cơ sinh học - Đạm Phú Mỹ số " + i,
                packagingName = "Bao 25 kg", quantity = 1, unitPrice = 45000m, lineTotalAmount = 45000m }),
        }), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var snapshot = new InvoiceSnapshot(1, new("Cửa hàng Vật tư Nông nghiệp Hai Thắng", "Ấp Thới Thuận, Thới Lai, Cần Thơ", "02923891234", "cuahang@example.test", "0123456789"),
            "Trần Thị Ánh", order, new(order.Id, order.TotalAmount, order.TotalAmount, 0, order.TotalAmount, 0, [], []));
        var bytes = new InvoicePdfRenderer().Render("INV-20261123-0001", now, snapshot);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes[..5])); Assert.True(bytes.Length > 1000);
        if (Environment.GetEnvironmentVariable("INVOICE_PDF_TEST_OUTPUT") is { } folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, $"invoice-{lines}-lines.pdf"), bytes);
        }
    }
}
