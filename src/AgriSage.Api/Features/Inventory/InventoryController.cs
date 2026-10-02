using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Inventory;

// Stock by lot and the stock movement ledger. Read: Admin, Store Owner, Sales. Lot status: Admin and Store Owner.
// Physical stock itself only changes through posted movements (goods receipts here; sales and stocktakes later).
[ApiController]
[Route("api/inventory")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class InventoryController(IInventoryService service) : ControllerBase
{
    [HttpGet("lots")]
    public async Task<ActionResult<PagedResult<InventoryLotResponse>>> ListLots(
        [FromQuery] InventoryLotListRequest request, CancellationToken cancellationToken) =>
        await service.ListLotsAsync(request, cancellationToken);

    [HttpGet("lots/{id:guid}")]
    public async Task<ActionResult<InventoryLotResponse>> GetLot(Guid id, CancellationToken cancellationToken) =>
        await service.GetLotAsync(id, cancellationToken);

    // ACTIVE / QUARANTINED / EXPIRED / BLOCKED; DEPLETED is derived, and an expired lot cannot be set ACTIVE.
    [HttpPost("lots/{id:guid}/status")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<InventoryLotResponse>> ChangeLotStatus(
        Guid id, ChangeLotStatusRequest request, CancellationToken cancellationToken) =>
        await service.ChangeLotStatusAsync(id, request, cancellationToken);

    [HttpGet("stock-movements")]
    public async Task<ActionResult<PagedResult<StockMovementListItem>>> ListMovements(
        [FromQuery] StockMovementListRequest request, CancellationToken cancellationToken) =>
        await service.ListMovementsAsync(request, cancellationToken);

    [HttpGet("stock-movements/{id:guid}")]
    public async Task<ActionResult<StockMovementResponse>> GetMovement(Guid id, CancellationToken cancellationToken) =>
        await service.GetMovementAsync(id, cancellationToken);
}
