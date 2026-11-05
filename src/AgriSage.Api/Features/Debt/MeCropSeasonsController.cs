using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Debt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Debt;

// F3.11 / CS-04: the season and estimated due date a Farmer's credit order placed today would get.
[ApiController]
[Authorize(Roles = ApiRoles.Farmer)]
public sealed class MeCropSeasonsController(ICropSeasonService service) : ControllerBase
{
    [HttpGet("api/me/crop-seasons/current")]
    public Task<MyCurrentCropSeasonResponse> Current(CancellationToken token) => service.MyCurrentAsync(token);
}
