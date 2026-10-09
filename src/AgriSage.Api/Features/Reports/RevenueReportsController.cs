using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Reports;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = ApiRoles.Manage)]
public sealed class RevenueReportsController(IRevenueReportService reports) : ControllerBase
{
    [HttpGet("revenue")]
    public Task<RevenueReportResponse> Revenue([FromQuery] RevenueReportRequest request, CancellationToken token) => reports.GetAsync(request, token);

    [HttpGet("revenue-summary")]
    public Task<RevenueSummaryResponse> Summary([FromQuery] RevenueSummaryRequest request, CancellationToken token) => reports.SummaryAsync(request, token);
}
