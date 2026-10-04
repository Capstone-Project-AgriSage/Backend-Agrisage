using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Orders;

// Quick counter sale (FLOW_1 section 9): Admin, Store Owner and Sales.
[ApiController]
[Route("api/counter-sales")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class CounterSalesController(ICounterSaleService service) : ControllerBase
{
    // Prices the lines and proposes the FEFO lots; nothing is saved.
    [HttpPost("preview")]
    public async Task<ActionResult<CounterSalePreviewResponse>> Preview(
        CounterSaleRequest request, CancellationToken cancellationToken) =>
        await service.PreviewAsync(request, cancellationToken);

    // Order, cash payment, reservation and pickup in one transaction: the order is COMPLETED and the payment PAID.
    [HttpPost]
    [ProducesResponseType<CounterSaleResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Sell(CounterSaleRequest request, CancellationToken cancellationToken)
    {
        var response = await service.SellAsync(request, cancellationToken);

        return Created($"/api/orders/{response.Order.Id}", response);
    }
}
