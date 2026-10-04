using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Customers;

[ApiController]
[Route("api/customer-groups")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class CustomerGroupsController(ICustomerGroupService service) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<CustomerGroupResponse>> List([FromQuery] CustomerGroupListRequest request, CancellationToken token) => service.ListAsync(request, token);
    [HttpGet("{id:guid}")]
    public Task<CustomerGroupResponse> Get(Guid id, CancellationToken token) => service.GetAsync(id, token);
    [HttpPost]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<IActionResult> Create(CustomerGroupRequest request, CancellationToken token)
    {
        var result = await service.CreateAsync(request, token);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }
    [HttpPut("{id:guid}")]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<CustomerGroupResponse> Update(Guid id, UpdateCustomerGroupRequest request, CancellationToken token) => service.UpdateAsync(id, request, token);
    [HttpPost("{id:guid}/activate")]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<CustomerGroupResponse> Activate(Guid id, CancellationToken token) => service.SetActiveAsync(id, true, token);
    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<CustomerGroupResponse> Deactivate(Guid id, CancellationToken token) => service.SetActiveAsync(id, false, token);
    [HttpPost("{id:guid}/set-default")]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<CustomerGroupResponse> SetDefault(Guid id, CancellationToken token) => service.SetDefaultAsync(id, token);
    [HttpPut("{id:guid}/credit-tier")]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<CustomerGroupResponse> CreditTier(Guid id, GroupCreditTierRequest request, CancellationToken token) => service.SetCreditTierAsync(id, request, token);
    [HttpPut("{id:guid}/price-list")]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<CustomerGroupResponse> PriceList(Guid id, GroupPriceListRequest request, CancellationToken token) => service.SetPriceListAsync(id, request, token);
    [HttpGet("{id:guid}/price-lists")]
    public Task<IReadOnlyList<GroupPriceListResponse>> PriceLists(Guid id, CancellationToken token) => service.PriceListsAsync(id, token);
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken token)
    {
        await service.DeleteAsync(id, token);
        return NoContent();
    }
}
