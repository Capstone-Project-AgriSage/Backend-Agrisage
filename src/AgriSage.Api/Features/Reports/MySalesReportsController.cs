using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Reports;

// The signed-in seller's own figures of a period (COUNTER_SALES.SELL); the user comes from the token, never the query.
[ApiController]
[Route("api/reports/my-sales")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class MySalesReportsController(IMySalesReportService reports) : ControllerBase
{
    [HttpGet]
    public Task<MySalesReportResponse> Get([FromQuery] MySalesReportRequest request, CancellationToken token) =>
        reports.GetAsync(request, token);
}
