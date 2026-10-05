using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Reports;

[ApiController]
[Authorize(Roles = ApiRoles.Manage)]
[Route("api/reports")]
public sealed class DebtReportsController(IDebtReportService service) : ControllerBase
{
    [HttpGet("debt-aging")]
    public Task<DebtAgingReportResponse> Aging([FromQuery] DebtAgingRequest request, CancellationToken token) => service.AgingAsync(request, token);
    [HttpGet("debt-collections")]
    public Task<DebtCollectionReportResponse> Collections([FromQuery] DebtCollectionRequest request, CancellationToken token) => service.CollectionsAsync(request, token);
    [HttpGet("debt-by-customer-group")]
    public Task<DebtByGroupReportResponse> Groups(CancellationToken token) => service.GroupsAsync(token);
}
