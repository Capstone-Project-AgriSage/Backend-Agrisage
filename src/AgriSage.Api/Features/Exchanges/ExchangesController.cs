using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Exchanges;
using AgriSage.Application.Features.Returns;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Exchanges;

[ApiController, Route("api/exchanges"), Authorize(Roles = ApiRoles.Operate)]
public sealed class ExchangesController(ExchangeService service) : ControllerBase
{
    [HttpGet("/api/orders/{id:guid}/exchangeable")]
    public async Task<ActionResult<ReturnableResponse>> Exchangeable(Guid id, CancellationToken token) => await service.ExchangeableAsync(id, false, token);
    [HttpPost]
    public async Task<IActionResult> Create(CreateExchangeRequest request, CancellationToken token)
    {
        var result = await service.CreateAsync(request, false, token);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }
    [HttpGet]
    public async Task<ActionResult<ExchangeListResponse>> List([FromQuery] ExchangeListRequest request, CancellationToken token) => await service.ListAsync(request, false, token);
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ExchangeResponse>> Get(Guid id, CancellationToken token) => await service.GetAsync(id, false, token);
    [HttpPost("{id:guid}/approve"), Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<ExchangeResponse>> Approve(Guid id, CancellationToken token) => await service.ApproveAsync(id, token);
    [HttpPost("{id:guid}/reject"), Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<ExchangeResponse>> Reject(Guid id, ReturnReasonRequest request, CancellationToken token) => await service.RejectAsync(id, request, token);
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<ExchangeResponse>> Cancel(Guid id, ReturnReasonRequest request, CancellationToken token) => await service.CancelAsync(id, request, false, token);
    [HttpPost("{id:guid}/receive")]
    public async Task<ActionResult<ExchangeResponse>> Receive(Guid id, CancellationToken token) => await service.ReceiveAsync(id, token);
    [HttpPut("{id:guid}/items/{itemId:guid}/inspection")]
    public async Task<ActionResult<ExchangeResponse>> Inspect(Guid id, Guid itemId, ReturnInspectionRequest request, CancellationToken token) => await service.InspectAsync(id, itemId, request, token);
    [HttpPost("{id:guid}/complete-inspection"), Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<ExchangeResponse>> FinishInspection(Guid id, CancellationToken token) => await service.FinishInspectionAsync(id, token);
    [HttpPost("{id:guid}/complete"), Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<ExchangeResponse>> Complete(Guid id, CompleteExchangeRequest request, CancellationToken token) => await service.CompleteAsync(id, request, token);
}

[ApiController, Route("api/me/exchanges"), Authorize(Roles = "FARMER")]
public sealed class MeExchangesController(ExchangeService service) : ControllerBase
{
    [HttpGet("/api/me/orders/{id:guid}/exchangeable")]
    public async Task<ActionResult<ReturnableResponse>> Exchangeable(Guid id, CancellationToken token) => await service.ExchangeableAsync(id, true, token);
    [HttpPost]
    public async Task<IActionResult> Create(CreateExchangeRequest request, CancellationToken token)
    {
        var result = await service.CreateAsync(request, true, token);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }
    [HttpGet]
    public async Task<ActionResult<ExchangeListResponse>> List([FromQuery] ExchangeListRequest request, CancellationToken token) => await service.ListAsync(request, true, token);
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ExchangeResponse>> Get(Guid id, CancellationToken token) => await service.GetAsync(id, true, token);
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<ExchangeResponse>> Cancel(Guid id, ReturnReasonRequest request, CancellationToken token) => await service.CancelAsync(id, request, true, token);
}
