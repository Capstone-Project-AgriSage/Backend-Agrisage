using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Reports;

// Reports (FLOW_1 section 10): Admin and Store Owner only.
[ApiController]
[Route("api/reports")]
[Authorize(Roles = ApiRoles.Manage)]
public sealed class ReportsController(ISalesReportService sales) : ControllerBase
{
    // Revenue at fulfillment, cost of goods, gross profit, returns and net sales for a period of Vietnam days.
    [HttpGet("sales")]
    public async Task<ActionResult<SalesReportResponse>> GetSales(
        [FromQuery] SalesReportRequest request, CancellationToken cancellationToken) =>
        await sales.GetAsync(request, cancellationToken);
}
