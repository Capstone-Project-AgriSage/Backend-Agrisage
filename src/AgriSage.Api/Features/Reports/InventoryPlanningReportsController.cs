using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Reports;

[ApiController, Authorize(Roles = ApiRoles.Manage)]
public sealed class InventoryPlanningReportsController(IInventoryPlanningReportService service,
    IInventoryReportService reports, IInventoryReportSpreadsheet spreadsheet) : ControllerBase
{
    [HttpGet("api/reports/inventory-turnover")]
    public Task<InventoryVelocityPage> Velocity([FromQuery] InventoryPlanningReportRequest request, CancellationToken token) => service.VelocityAsync(request, token);
    [HttpGet("api/inventory/expiry-risk")]
    public Task<ExpiryRiskPage> ExpiryRisk([FromQuery] InventoryPlanningReportRequest request, CancellationToken token) => service.ExpiryRiskAsync(request, token);
    [HttpGet("api/reports/inventory-movement/export")]
    public async Task<IActionResult> Export([FromQuery] InventoryMovementReportRequest request, CancellationToken token)
    {
        var report = await reports.GetMovementAsync(request, token);
        return File(spreadsheet.Export(report), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"nhap-xuat-ton-{report.FromDate:yyyyMMdd}-{report.ToDate:yyyyMMdd}.xlsx");
    }
}
