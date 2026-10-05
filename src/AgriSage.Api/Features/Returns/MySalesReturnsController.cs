using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Returns;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Returns;

[ApiController]
[Route("api/me/returns")]
[Authorize(Roles = "FARMER")]
public sealed class MySalesReturnsController(IMySalesReturnService service) : ControllerBase
{
    [HttpGet("/api/me/orders/{id:guid}/returnable")]
    public async Task<ActionResult<ReturnableResponse>> Returnable(Guid id, CancellationToken token) => await service.ReturnableAsync(id, token);
    [HttpPost]
    [ProducesResponseType<SalesReturnResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateReturnRequest request, CancellationToken token)
    {
        var response = await service.CreateAsync(request, token);
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }
    [HttpGet]
    public async Task<ActionResult<PagedResult<SalesReturnListItem>>> List([FromQuery] PaginationRequest request, CancellationToken token) => await service.ListAsync(request, token);
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SalesReturnResponse>> Get(Guid id, CancellationToken token) => await service.GetAsync(id, token);
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<SalesReturnResponse>> Cancel(Guid id, ReturnReasonRequest request, CancellationToken token) => await service.CancelAsync(id, request, token);
}
