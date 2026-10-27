using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Reports;

[ApiController]
[Route("api/reports/counter-sales-today")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class CounterSalesReportsController(ICounterSalesTodayService reports) : ControllerBase
{
    [HttpGet]
    public Task<CounterSalesTodayResponse> Today(CancellationToken token) => reports.GetAsync(token);
}
