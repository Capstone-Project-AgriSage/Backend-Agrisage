using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Advisory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Advisory;

// The Farmer's advisory: pick a problem, get products with quantity for the field (api-flows/ADVISORY.md section 5).
// HTTP boundary only: identity comes from the JWT. Adding to the cart uses the normal cart routes.
[ApiController]
[Route("api/me/advisory")]
[Authorize(Roles = ApiRoles.Farmer)]
public sealed class MeAdvisoryController(IMyAdvisoryService service) : ControllerBase
{
    // Accent-insensitive search over the active problems of the store; an empty search lists all of them.
    [HttpGet("problems")]
    public async Task<ActionResult<PagedResult<AdvisoryProblem>>> Problems(
        [FromQuery] AdvisoryProblemListRequest request, CancellationToken cancellationToken) =>
        await service.ProblemsAsync(request, cancellationToken);

    // Products for the problem, priced for the Farmer, with quantity and stock status; the call is logged as a session.
    [HttpPost("recommendations")]
    public async Task<ActionResult<AdvisoryRecommendationResponse>> Recommend(
        AdvisoryRecommendationRequest request, CancellationToken cancellationToken) =>
        await service.RecommendAsync(request, cancellationToken);
}
