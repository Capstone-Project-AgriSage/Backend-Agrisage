using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Debt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Debt;

// F3.20: starting debt reminders for the store. Owner only (DEBT_POLICY.MANAGE, not delegable); dryRun (the default) writes
// nothing and sends nothing.
[ApiController]
[Route("api/debt-reminders")]
[Authorize(Roles = ApiRoles.Manage)]
public sealed class DebtRemindersController(IDebtReminderStartService service) : ControllerBase
{
    [HttpPost("initialize")]
    public Task<DebtReminderStartResponse> Initialize(DebtReminderStartRequest request, CancellationToken token) =>
        service.StartAsync(request, token);
}
