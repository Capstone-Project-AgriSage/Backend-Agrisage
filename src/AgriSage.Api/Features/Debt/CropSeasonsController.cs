using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Debt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Debt;

// F3.11: debt-collection crop seasons. Reading is open to Sale staff (CROP_SEASONS.READ); creating and changing seasons is
// owner-only (CROP_SEASONS.MANAGE, not delegable), because season dates decide every credit customer's due date.
[ApiController]
[Route("api/crop-seasons")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class CropSeasonsController(ICropSeasonService service, IDebtRolloverService rollovers,
    ICropSeasonClosingService closing) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<CropSeasonResponse>> List([FromQuery] CropSeasonListRequest request, CancellationToken token) =>
        service.ListAsync(request, token);

    [HttpGet("{id:guid}")]
    public Task<CropSeasonResponse> Get(Guid id, CancellationToken token) => service.GetAsync(id, token);

    [HttpPost]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<IActionResult> Create(CreateCropSeasonRequest request, CancellationToken token)
    {
        var season = await service.CreateAsync(request, token);
        return CreatedAtAction(nameof(Get), new { id = season.Id }, season);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<CropSeasonResponse> Update(Guid id, UpdateCropSeasonRequest request, CancellationToken token) =>
        service.UpdateAsync(id, request, token);

    // F3.15: roll the season's debt over to a later season (DEBT.ROLLOVER; each entry follows the F3.14 rules).
    [HttpPost("{id:guid}/debt/rollover")]
    public Task<CropSeasonRolloverResponse> RolloverDebt(Guid id, CropSeasonRolloverRequest request, CancellationToken token) =>
        rollovers.RolloverSeasonAsync(id, request, token);

    // F3.19: what still blocks closing (D11), for the closing screen.
    [HttpGet("{id:guid}/closing-check")]
    public Task<CropSeasonClosingCheckResponse> ClosingCheck(Guid id, CancellationToken token) => closing.ClosingCheckAsync(id, token);

    // F3.19: owner-only (CROP_SEASONS.MANAGE, not delegable); a closed season never changes or reopens.
    [HttpPost("{id:guid}/close")]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<CropSeasonResponse> Close(Guid id, CloseCropSeasonRequest request, CancellationToken token) =>
        closing.CloseAsync(id, request, token);

    // F3.19: customers on time in the last closed seasons (D12); read-only, the limit is changed through the credit API.
    [HttpGet("{id:guid}/limit-suggestions")]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<LimitSuggestionsResponse> LimitSuggestions(Guid id, [FromQuery] LimitSuggestionsRequest request, CancellationToken token) =>
        closing.LimitSuggestionsAsync(id, request, token);
}
