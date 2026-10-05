using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Credit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Customers;

[ApiController]
[Authorize(Roles = ApiRoles.Farmer)]
public sealed class MeCreditController(ICustomerCreditService service) : ControllerBase
{
    [HttpGet("api/me/credit")]
    public Task<MyCreditSummaryResponse> Credit(CancellationToken token) => service.GetOwnAsync(token);
}
