using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Debt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Debt;

// F3.12: the store debt policy. Reading is open to Sale staff (DEBT_POLICY.READ); changing it is owner-only
// (DEBT_POLICY.MANAGE, not delegable), because it sets grace, rollover limits and reminders for every credit customer.
[ApiController]
[Route("api/debt-policy")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class DebtPolicyController(IDebtPolicyService service) : ControllerBase
{
    [HttpGet]
    public Task<DebtPolicyResponse> Get(CancellationToken token) => service.GetAsync(token);

    [HttpPut]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<DebtPolicyResponse> Update(UpdateDebtPolicyRequest request, CancellationToken token) =>
        service.UpdateAsync(request, token);
}
