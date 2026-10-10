using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Diagnosis;
using AgriSage.Application.Features.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgriSage.Api.Features.Diagnosis;

// The Farmer's own AI diagnosis cases (AI_DIAGNOSIS.md section 7). HTTP boundary only: identity comes from the JWT.
[ApiController]
[Route("api/me/diagnosis-cases")]
[Authorize(Roles = ApiRoles.Farmer)]
public sealed class MeDiagnosisCasesController(IMyDiagnosisCaseService service) : ControllerBase
{
    // multipart/form-data: field "image" (JPEG, PNG or WebP, at most 5 MB) and an optional text field "note".
    // The answer is the case after the AI ran: waiting for a reviewer in both the AI_COMPLETED and the FAILED case.
    [HttpPost]
    [EnableRateLimiting(RateLimitingExtensions.UploadPolicy)]
    [RequestSizeLimit(ImageRules.MaxDiagnosisBytes + 512 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImageRules.MaxDiagnosisBytes + 512 * 1024)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<MyDiagnosisCaseResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(IFormFile image, [FromForm] string? note, CancellationToken cancellationToken)
    {
        await using var stream = image.OpenReadStream();
        var response = await service.CreateAsync(stream, image.FileName, note, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<MyDiagnosisCaseListItem>>> List(
        [FromQuery] MyDiagnosisCaseListRequest request, CancellationToken cancellationToken) =>
        await service.ListAsync(request, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MyDiagnosisCaseResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await service.GetAsync(id, cancellationToken);

    // Allowed until a reviewer has decided; semantic cancel, the case and its photo are kept.
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<MyDiagnosisCaseResponse>> Cancel(
        Guid id, CancelDiagnosisRequest? request, CancellationToken cancellationToken) =>
        await service.CancelAsync(id, request ?? new CancelDiagnosisRequest(), cancellationToken);
}
