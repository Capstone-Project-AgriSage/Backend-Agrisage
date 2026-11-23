using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Reports;

// Advice given and photo diagnoses of a period: Admin and Store Owner (REPORTS.READ).
[ApiController]
[Route("api/reports/advisory-diagnosis")]
[Authorize(Roles = ApiRoles.Manage)]
public sealed class AdvisoryDiagnosisReportsController(IAdvisoryDiagnosisReportService reports) : ControllerBase
{
    [HttpGet]
    public Task<AdvisoryDiagnosisReportResponse> Get([FromQuery] AdvisoryDiagnosisReportRequest request, CancellationToken token) =>
        reports.GetAsync(request, token);
}
