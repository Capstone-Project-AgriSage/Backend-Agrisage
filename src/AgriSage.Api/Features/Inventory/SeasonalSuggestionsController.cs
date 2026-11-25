using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Inventory;

// F4.7 seasonal product suggestions (FLOW_4 "Seasonal product suggestions"): read-only. The reminder prepares the runs.
// The roles admit owners and Sale staff; the permission map then requires SEASONAL_RULES.READ, which a Sale member has
// only when delegated.
[ApiController, Route("api/inventory/seasonal-suggestions"), Authorize(Roles = ApiRoles.Operate)]
public sealed class SeasonalSuggestionsController(ISeasonalSuggestionService service) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<SeasonalSuggestionListItem>> List([FromQuery] SeasonalSuggestionListRequest request,
        CancellationToken token) => service.ListAsync(request, token);

    [HttpGet("{id:guid}")]
    public Task<SeasonalSuggestionResponse> Get(Guid id, CancellationToken token) => service.GetAsync(id, token);
}
