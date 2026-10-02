using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Dtos.Responses;
using AgriSage.Application.Features.Products.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Products;

// Units are reference data (seeded); read-only.
[ApiController]
[Route("api/units")]
[Authorize(Roles = ApiRoles.Read)]
public sealed class UnitsController(IUnitService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<UnitResponse>>> List(
        [FromQuery] UnitListRequest request, CancellationToken cancellationToken) =>
        await service.ListAsync(request, cancellationToken);
}
