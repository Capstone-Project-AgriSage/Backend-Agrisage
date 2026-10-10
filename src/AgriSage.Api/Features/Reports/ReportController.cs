using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Reports;

// Sales and operational reports: Admin and Store Owner only.
[ApiController]
[Route("api/reports")]
[Authorize(Roles = ApiRoles.Manage)]
public sealed class ReportController(ISalesReportService sales, IOperationalReportService reports) : ControllerBase
{
    // Revenue at fulfillment, cost of goods, gross profit, returns and net sales for a period of Vietnam days.
    [HttpGet("sales")]
    public Task<SalesReportResponse> GetSales([FromQuery] SalesReportRequest request, CancellationToken token) => sales.GetAsync(request, token);

    [HttpGet("orders")]
    public Task<OrderReportResponse> Orders([FromQuery] OrderReportRequest request, CancellationToken token) => reports.OrdersAsync(request, token);

    [HttpGet("payments")]
    public Task<PaymentReportResponse> Payments([FromQuery] PaymentReportRequest request, CancellationToken token) => reports.PaymentsAsync(request, token);

    [HttpGet("purchases")]
    public Task<PurchaseReportResponse> Purchases([FromQuery] PurchaseReportRequest request, CancellationToken token) => reports.PurchasesAsync(request, token);

    [HttpGet("returns")]
    public Task<ReturnReportResponse> Returns([FromQuery] ReturnReportRequest request, CancellationToken token) => reports.ReturnsAsync(request, token);

    [HttpGet("refunds")]
    public Task<RefundReportResponse> Refunds([FromQuery] RefundReportRequest request, CancellationToken token) => reports.RefundsAsync(request, token);

    [HttpGet("credit-exposure")]
    public Task<CreditExposureReportResponse> Credit(CancellationToken token) => reports.CreditAsync(token);
}
