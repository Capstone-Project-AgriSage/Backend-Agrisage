using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Reports;

// Delivery report (FLOW_2 §9): Admin and Store Owner only.
[ApiController]
[Route("api/reports")]
[Authorize(Roles = ApiRoles.Manage)]
public sealed class DeliveryReportsController(IDeliveryReportService deliveries) : ControllerBase
{
    // Attempts per day or per delivery staff, failure reasons and incidents for a period of Vietnam days.
    [HttpGet("deliveries")]
    public async Task<ActionResult<DeliveryReportResponse>> GetDeliveries(
        [FromQuery] DeliveryReportRequest request, CancellationToken cancellationToken) =>
        await deliveries.GetAsync(request, cancellationToken);
}
