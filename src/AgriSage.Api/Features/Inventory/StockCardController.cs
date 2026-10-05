using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Inventory;

[ApiController]
[Route("api/inventory/stock-card")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class StockCardController(IInventoryReportService reports) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<StockCardResponse>> Get([FromQuery] StockCardRequest request, CancellationToken cancellationToken) =>
        await reports.GetStockCardAsync(request, cancellationToken);
}
