using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Credit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Customers;

[ApiController]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class CreditEligibilityController(ICreditEligibilityService service) : ControllerBase
{
    [HttpPost("api/customers/{farmerProfileId:guid}/credit-eligibility")]
    public Task<CreditEligibilityResponse> Check(Guid farmerProfileId, CreditEligibilityRequest request, CancellationToken token) =>
        service.CheckAsync(farmerProfileId, request.OrderAmount, token);
}
