using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Reports;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = ApiRoles.Manage)]
public sealed class InventoryReportsController(IInventoryReportService reports) : ControllerBase
{
    [HttpGet("inventory-movement")]
    public async Task<ActionResult<InventoryMovementReportResponse>> GetMovement([FromQuery] InventoryMovementReportRequest request, CancellationToken cancellationToken) =>
        await reports.GetMovementAsync(request, cancellationToken);

    [HttpGet("inventory-valuation")]
    public async Task<ActionResult<InventoryValuationReportResponse>> GetValuation([FromQuery] InventoryValuationReportRequest request, CancellationToken cancellationToken) =>
        await reports.GetValuationAsync(request, cancellationToken);
}
