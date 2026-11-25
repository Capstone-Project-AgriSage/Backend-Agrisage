using System.Globalization;
using AgriSage.Application.Features.Invoices;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AgriSage.Infrastructure.Invoices;

public sealed class InvoicePdfRenderer : IInvoicePdfRenderer
{
    public InvoicePdfRenderer()
    {
        // This repository is a capstone/learning project. See INVOICES.md before commercial deployment.
        QuestPDF.Settings.License = LicenseType.Community;
    }
    private static string Money(decimal value) => value.ToString(value % 1 == 0 ? "N0" : "N2", CultureInfo.GetCultureInfo("vi-VN")) + " đ";
    private static string Date(DateTimeOffset value) => value.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm");

    public byte[] Render(string invoiceNumber, DateTimeOffset issuedAt, InvoiceSnapshot snapshot)
    {
        var order = snapshot.Order;
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4); page.Margin(36); page.DefaultTextStyle(style => style.FontSize(10).FontFamily("Lato"));
            page.Header().PaddingBottom(18).Column(column =>
            {
                column.Item().ShowOnce().Column(first =>
                {
                    first.Item().Text(snapshot.Store.Name).FontSize(17).Bold().FontColor("#166534");
                    first.Item().Text(snapshot.Store.Address);
                    if (snapshot.Store.Phone is { } phone) first.Item().Text("Điện thoại: " + phone);
                    if (snapshot.Store.Email is { } email) first.Item().Text("Email: " + email);
                    if (snapshot.Store.TaxCode is { } tax) first.Item().Text("Mã số thuế: " + tax);
                    first.Item().PaddingTop(14).Text("HÓA ĐƠN BÁN HÀNG").FontSize(21).Bold();
                    first.Item().Text(invoiceNumber + " | Ngày lập: " + Date(issuedAt));
                });
                column.Item().SkipOnce().Text("HÓA ĐƠN BÁN HÀNG | " + invoiceNumber).Bold().FontColor("#166534");
            });
            page.Content().Column(column =>
            {
                column.Spacing(8);
                column.Item().Text("Đơn hàng: " + order.OrderNumber + " | Ngày bán: " + Date(order.CompletedAt ?? order.CreatedAt));
                column.Item().Text("Khách hàng: " + order.CustomerName);
                if (order.CustomerPhone is { } phone) column.Item().Text("Điện thoại: " + phone);
                column.Item().Text("Nhân viên lập: " + snapshot.CashierName);
                column.Item().Text("Hình thức: " + (order.SettlementType == "CREDIT" ? "Mua chịu" : "Trả đủ"));
                column.Item().PaddingVertical(8).Table(table =>
                {
                    table.ColumnsDefinition(columns => { columns.RelativeColumn(5); columns.RelativeColumn(1); columns.RelativeColumn(2); columns.RelativeColumn(2); });
                    table.Header(header =>
                    {
                        foreach (var title in new[] { "Sản phẩm / Quy cách", "SL", "Đơn giá", "Thành tiền" })
                            header.Cell().Background("#f0fdf4").BorderBottom(1).BorderColor("#166534").Padding(7).Text(title).Bold();
                    });
                    foreach (var item in order.Items)
                    {
                        table.Cell().Element(Cell).Column(c => { c.Item().Text(item.ProductName).Bold(); c.Item().Text(item.Sku + " | " + item.PackagingName).FontSize(9); });
                        table.Cell().Element(Cell).AlignRight().Text(item.Quantity.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")));
                        table.Cell().Element(Cell).AlignRight().Text(Money(item.UnitPrice));
                        table.Cell().Element(Cell).AlignRight().Text(Money(item.LineTotalAmount));
                    }
                });
                foreach (var payment in snapshot.Payments.Payments.Where(x => x.Status is "PAID" or "REFUNDED" or "PARTIALLY_REFUNDED"))
                    column.Item().Text("Phiếu thu: " + payment.PaymentNumber + " | " + (payment.PaymentMethod switch { "CASH" => "Tiền mặt", "EXCHANGE_CREDIT" => "Bù trừ đổi hàng", _ => "Chuyển khoản" })).FontSize(9);
                if (order.Note is { } note) column.Item().EnsureSpace(40).PaddingTop(8).Text("Ghi chú: " + note);
                column.Item().ShowEntire().Column(totals =>
                {
                    totals.Spacing(6);
                    Total(totals, "Tổng cộng", order.TotalAmount, true);
                    Total(totals, "Đã thanh toán lúc lập hóa đơn", snapshot.Payments.PaidAmount);
                    Total(totals, "Còn phải thanh toán lúc lập hóa đơn", snapshot.Payments.RemainingToPay);
                    totals.Item().PaddingTop(16).Text("Cảm ơn quý khách và hẹn gặp lại!").AlignCenter();
                });
            });
            page.Footer().PaddingTop(16).Row(row =>
            {
                row.RelativeItem().Text(invoiceNumber).FontSize(9).FontColor(Colors.Grey.Darken1);
                row.AutoItem().Text(text => { text.Span("Trang "); text.CurrentPageNumber(); text.Span(" / "); text.TotalPages(); });
            });
        })).GeneratePdf();
    }

    private static IContainer Cell(IContainer container) => container.BorderBottom(0.5f).BorderColor("#d1d5db").Padding(7);
    private static void Total(ColumnDescriptor column, string label, decimal amount, bool prominent = false) => column.Item().Row(row =>
    {
        var size = prominent ? 14 : 10;
        row.RelativeItem().Text(label).FontSize(size);
        row.AutoItem().Text(Money(amount)).FontSize(size).Bold();
    });
}
