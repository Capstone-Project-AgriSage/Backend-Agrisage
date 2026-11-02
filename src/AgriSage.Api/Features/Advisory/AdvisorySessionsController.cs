using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Advisory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Advisory;

// What the store advised its Farmers and whether it led to a purchase (api-flows/ADVISORY.md section 7). Read only.
[ApiController]
[Route("api/advisory/sessions")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class AdvisorySessionsController(IAdvisorySessionService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AdvisorySessionResponse>>> List(
        [FromQuery] AdvisorySessionListRequest request, CancellationToken cancellationToken) =>
        await service.ListAsync(request, cancellationToken);
}
