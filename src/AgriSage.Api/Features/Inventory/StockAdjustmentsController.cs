using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Inventory;

[ApiController]
[Route("api/inventory/adjustments")]
[Authorize(Roles = ApiRoles.Manage)]
public sealed class StockAdjustmentsController(IStockAdjustmentService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<StockMovementResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(StockAdjustmentRequest request, CancellationToken token)
    {
        var response = await service.CreateAsync(request, token);
        return CreatedAtAction(nameof(InventoryController.GetMovement), "Inventory", new { id = response.Id }, response);
    }
}
