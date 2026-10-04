using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Credit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Customers;

[ApiController]
[Route("api/customers/{farmerProfileId:guid}/credit")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class CustomerCreditController(ICustomerCreditService service) : ControllerBase
{
    [HttpGet]
    public Task<CreditSummaryResponse> Get(Guid farmerProfileId, CancellationToken token) => service.GetAsync(farmerProfileId, token);
    [HttpPost]
    public async Task<IActionResult> Create(Guid farmerProfileId, CreateCustomerCreditRequest request, CancellationToken token)
    {
        var response = await service.CreateAsync(farmerProfileId, request, token);
        return CreatedAtAction(nameof(Get), new { farmerProfileId }, response);
    }
    [HttpPut("limit")]
    public Task<CreditSummaryResponse> Limit(Guid farmerProfileId, CustomerCreditLimitRequest request, CancellationToken token) => service.LimitAsync(farmerProfileId, request, token);
    [HttpPost("activate")]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<CreditSummaryResponse> Activate(Guid farmerProfileId, CustomerCreditStatusRequest request, CancellationToken token) => service.StatusAsync(farmerProfileId, "ACTIVE", request, token);
    [HttpPost("suspend")]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<CreditSummaryResponse> Suspend(Guid farmerProfileId, CustomerCreditStatusRequest request, CancellationToken token) => service.StatusAsync(farmerProfileId, "SUSPENDED", request, token);
    [HttpPost("block")]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<CreditSummaryResponse> Block(Guid farmerProfileId, CustomerCreditStatusRequest request, CancellationToken token) => service.StatusAsync(farmerProfileId, "BLOCKED", request, token);
    [HttpGet("history")]
    public Task<IReadOnlyList<CreditLimitHistoryResponse>> History(Guid farmerProfileId, CancellationToken token) => service.HistoryAsync(farmerProfileId, token);
    [HttpGet("reservations")]
    public Task<IReadOnlyList<CreditReservationResponse>> Reservations(Guid farmerProfileId, [FromQuery] bool activeOnly, CancellationToken token) => service.ReservationsAsync(farmerProfileId, activeOnly, token);
}
