using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Reports;

[ApiController]
[Authorize(Roles = ApiRoles.Manage)]
[Route("api/reports")]
public sealed class DebtReportsController(IDebtReportService service, ICropSeasonClosingService seasons) : ControllerBase
{
    [HttpGet("debt-aging")]
    public Task<DebtAgingReportResponse> Aging([FromQuery] DebtAgingRequest request, CancellationToken token) => service.AgingAsync(request, token);
    [HttpGet("debt-collections")]
    public Task<DebtCollectionReportResponse> Collections([FromQuery] DebtCollectionRequest request, CancellationToken token) => service.CollectionsAsync(request, token);
    [HttpGet("debt-by-customer-group")]
    public Task<DebtByGroupReportResponse> Groups(CancellationToken token) => service.GroupsAsync(token);
    // F3.19: debt by origin season and by collection season (D9); a closed season shows its closing figures.
    [HttpGet("debt-by-season")]
    public Task<PagedResult<DebtBySeasonItem>> BySeason([FromQuery] DebtBySeasonRequest request, CancellationToken token) =>
        seasons.ReportAsync(request, token);
}
