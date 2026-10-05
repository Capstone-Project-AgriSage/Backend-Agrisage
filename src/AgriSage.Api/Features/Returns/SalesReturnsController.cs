using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Returns;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Returns;

[ApiController]
[Route("api/returns")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class SalesReturnsController(ISalesReturnService service) : ControllerBase
{
    [HttpGet("/api/orders/{id:guid}/returnable")]
    public async Task<ActionResult<ReturnableResponse>> Returnable(Guid id, CancellationToken token) => await service.ReturnableAsync(id, token);
    [HttpPost]
    [ProducesResponseType<SalesReturnResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateReturnRequest request, CancellationToken token)
    {
        var response = await service.CreateAsync(request, token);
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }
    [HttpGet]
    public async Task<ActionResult<PagedResult<SalesReturnListItem>>> List([FromQuery] SalesReturnListRequest request, CancellationToken token) =>
        await service.ListAsync(request, token);
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SalesReturnResponse>> Get(Guid id, CancellationToken token) => await service.GetAsync(id, token);
    [HttpPost("{id:guid}/items")]
    public async Task<ActionResult<SalesReturnResponse>> AddItem(Guid id, ReturnItemRequest request, CancellationToken token) => await service.AddItemAsync(id, request, token);
    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<SalesReturnResponse>> RemoveItem(Guid id, Guid itemId, CancellationToken token) => await service.RemoveItemAsync(id, itemId, token);
    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<SalesReturnResponse>> Approve(Guid id, CancellationToken token) => await service.ApproveAsync(id, token);
    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<SalesReturnResponse>> Reject(Guid id, ReturnReasonRequest request, CancellationToken token) => await service.RejectAsync(id, request, token);
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<SalesReturnResponse>> Cancel(Guid id, ReturnReasonRequest request, CancellationToken token) => await service.CancelAsync(id, request, token);
    [HttpPost("{id:guid}/receive")]
    public async Task<ActionResult<SalesReturnResponse>> Receive(Guid id, CancellationToken token) => await service.ReceiveAsync(id, token);
    [HttpPut("{id:guid}/items/{itemId:guid}/inspection")]
    public async Task<ActionResult<SalesReturnResponse>> Inspect(Guid id, Guid itemId, ReturnInspectionRequest request, CancellationToken token) => await service.InspectAsync(id, itemId, request, token);
    [HttpPost("{id:guid}/complete-inspection")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<SalesReturnResponse>> CompleteInspection(Guid id, CancellationToken token) => await service.CompleteInspectionAsync(id, token);
}
