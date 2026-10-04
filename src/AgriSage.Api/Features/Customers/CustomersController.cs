using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Customers;

[ApiController]
[Route("api/customers")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class CustomersController(ICustomerService service) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<CustomerResponse>> List([FromQuery] CustomerListRequest request, CancellationToken token) => service.ListAsync(request, token);

    [HttpGet("{farmerProfileId:guid}")]
    public Task<CustomerResponse> Get(Guid farmerProfileId, CancellationToken token) => service.GetAsync(farmerProfileId, token);

    [HttpPost]
    public async Task<IActionResult> Create(CreateCustomerRequest request, CancellationToken token)
    {
        var response = await service.CreateAsync(request, token);
        return CreatedAtAction(nameof(Get), new { farmerProfileId = response.Id }, response);
    }

    [HttpPut("{farmerProfileId:guid}")]
    public Task<CustomerResponse> Update(Guid farmerProfileId, UpdateCustomerRequest request, CancellationToken token) => service.UpdateAsync(farmerProfileId, request, token);

    [HttpPost("{farmerProfileId:guid}/status")]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<CustomerResponse> Status(Guid farmerProfileId, CustomerStatusRequest request, CancellationToken token) => service.SetStatusAsync(farmerProfileId, request, token);

    [HttpPut("{farmerProfileId:guid}/group")]
    public Task<CustomerResponse> Group(Guid farmerProfileId, AssignCustomerGroupRequest request, CancellationToken token) => service.AssignGroupAsync(farmerProfileId, request, token);

    [HttpGet("{farmerProfileId:guid}/group-history")]
    public Task<IReadOnlyList<GroupAssignmentResponse>> GroupHistory(Guid farmerProfileId, CancellationToken token) => service.GroupHistoryAsync(farmerProfileId, token);

    [HttpGet("{farmerProfileId:guid}/orders")]
    public Task<PagedResult<CustomerOrderResponse>> Orders(Guid farmerProfileId, [FromQuery] CustomerOrderListRequest request, CancellationToken token) => service.OrdersAsync(farmerProfileId, request, token);

    [HttpGet("{farmerProfileId:guid}/debts")]
    public Task<CustomerDebtSummaryResponse> Debts(Guid farmerProfileId, CancellationToken token) => service.DebtSummaryAsync(farmerProfileId, token);

    [HttpGet("{farmerProfileId:guid}/payments")]
    public Task<PagedResult<CustomerPaymentResponse>> Payments(Guid farmerProfileId, [FromQuery] PaymentListRequest request, CancellationToken token) => service.PaymentsAsync(farmerProfileId, request, token);
}
