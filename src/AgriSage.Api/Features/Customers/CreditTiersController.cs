using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Credit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Customers;

[ApiController]
[Route("api/credit-tiers")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class CreditTiersController(ICustomerCreditService service) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<CreditTierResponse>> List([FromQuery] CustomerGroupListRequest request, CancellationToken token) => service.TiersAsync(request, token);
    [HttpGet("{id:guid}")]
    public Task<CreditTierResponse> Get(Guid id, CancellationToken token) => service.TierAsync(id, token);
    [HttpPost]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<IActionResult> Create(CreditTierRequest request, CancellationToken token)
    {
        var response = await service.CreateTierAsync(request, token);
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }
    [HttpPut("{id:guid}")]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<CreditTierResponse> Update(Guid id, UpdateCreditTierRequest request, CancellationToken token) => service.UpdateTierAsync(id, request, token);
    [HttpPost("{id:guid}/activate")]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<CreditTierResponse> Activate(Guid id, CancellationToken token) => service.SetTierActiveAsync(id, true, token);
    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<CreditTierResponse> Deactivate(Guid id, CancellationToken token) => service.SetTierActiveAsync(id, false, token);
}
