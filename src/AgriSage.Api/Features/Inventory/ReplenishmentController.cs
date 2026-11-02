using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Inventory;

[ApiController, Route("api/inventory/replenishment"), Authorize(Roles = ApiRoles.Manage)]
public sealed class ReplenishmentController(IReplenishmentService service) : ControllerBase
{
    [HttpGet("suggestions")]
    public Task<ReplenishmentPage> Suggestions([FromQuery] ReplenishmentRequest request, CancellationToken token) => service.GetAsync("suggestions", request, token);
    [HttpGet("stockout")]
    public Task<ReplenishmentPage> Stockout([FromQuery] ReplenishmentRequest request, CancellationToken token) => service.GetAsync("stockout", request, token);
    [HttpGet("forecast")]
    public Task<ReplenishmentPage> Forecast([FromQuery] ReplenishmentRequest request, CancellationToken token) => service.GetAsync("forecast", request, token);
    [HttpGet("seasonal")]
    public Task<ReplenishmentPage> Seasonal([FromQuery] ReplenishmentRequest request, CancellationToken token) => service.GetAsync("seasonal", request, token);
}
