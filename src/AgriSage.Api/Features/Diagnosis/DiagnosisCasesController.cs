using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Diagnosis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Diagnosis;

// Reviewer side of AI diagnosis (AI_DIAGNOSIS.md section 8). The route's permission code is checked by the permission
// filter; deciding a case also needs the member's can_review_ai flag, checked in the service.
[ApiController]
[Route("api/diagnosis-cases")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class DiagnosisCasesController(IDiagnosisCaseService service) : ControllerBase
{
    // The review queue: oldest first.
    [HttpGet]
    public async Task<ActionResult<PagedResult<DiagnosisCaseListItem>>> List(
        [FromQuery] DiagnosisCaseListRequest request, CancellationToken cancellationToken) =>
        await service.ListAsync(request, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DiagnosisCaseResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await service.GetAsync(id, cancellationToken);

    [HttpPost("{id:guid}/start-review")]
    public async Task<ActionResult<DiagnosisCaseResponse>> StartReview(Guid id, CancellationToken cancellationToken) =>
        await service.StartReviewAsync(id, cancellationToken);

    [HttpPost("{id:guid}/review")]
    public async Task<ActionResult<DiagnosisCaseResponse>> Review(
        Guid id, ReviewRequest request, CancellationToken cancellationToken) =>
        await service.ReviewAsync(id, request, cancellationToken);

    // Runs the AI again on a case that is submitted or failed; answers 503 when the AI service cannot be reached.
    [HttpPost("{id:guid}/rerun-ai")]
    public async Task<ActionResult<DiagnosisCaseResponse>> RerunAi(Guid id, CancellationToken cancellationToken) =>
        await service.RerunAiAsync(id, cancellationToken);

    [HttpPost("{id:guid}/recommendations")]
    [ProducesResponseType<RecommendationResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Recommend(
        Guid id, RecommendationRequest request, CancellationToken cancellationToken)
    {
        var response = await service.RecommendAsync(id, request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id }, response);
    }

    // Semantic delete: the recommendation is deactivated, the row is kept.
    [HttpDelete("{id:guid}/recommendations/{recommendationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unrecommend(Guid id, Guid recommendationId, CancellationToken cancellationToken)
    {
        await service.UnrecommendAsync(id, recommendationId, cancellationToken);

        return NoContent();
    }
}
