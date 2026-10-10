using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Diagnosis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Diagnosis;

// Disease content and treatment guidance (AI_DIAGNOSIS.md section 10). Reading: Admin, Store Owner, Sales (reviewers pick
// treatments from it); writing: Admin and Store Owner. The seeded code and healthy flag cannot change.
[ApiController]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class DiseasesController(IDiseaseService service) : ControllerBase
{
    [HttpGet("api/diseases")]
    public async Task<ActionResult<IReadOnlyList<DiseaseResponse>>> List(
        [FromQuery] bool? isActive, CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(isActive, cancellationToken));

    [HttpPut("api/diseases/{id:guid}")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<DiseaseResponse>> Update(
        Guid id, DiseaseUpdateRequest request, CancellationToken cancellationToken) =>
        await service.UpdateAsync(id, request, cancellationToken);

    [HttpPost("api/diseases/{id:guid}/treatments")]
    [Authorize(Roles = ApiRoles.Manage)]
    [ProducesResponseType<TreatmentItem>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTreatment(Guid id, TreatmentRequest request, CancellationToken cancellationToken)
    {
        var response = await service.CreateTreatmentAsync(id, request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, response);
    }
}

[ApiController]
[Authorize(Roles = ApiRoles.Manage)]
public sealed class DiseaseTreatmentsController(IDiseaseService service) : ControllerBase
{
    [HttpPut("api/disease-treatments/{id:guid}")]
    public async Task<ActionResult<TreatmentItem>> Update(
        Guid id, TreatmentRequest request, CancellationToken cancellationToken) =>
        await service.UpdateTreatmentAsync(id, request, cancellationToken);

    [HttpPost("api/disease-treatments/{id:guid}/activate")]
    public async Task<ActionResult<TreatmentItem>> Activate(Guid id, CancellationToken cancellationToken) =>
        await service.SetTreatmentActiveAsync(id, true, cancellationToken);

    // Semantic delete: the treatment is deactivated (recommendations that used it keep working as history).
    [HttpDelete("api/disease-treatments/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await service.SetTreatmentActiveAsync(id, false, cancellationToken);

        return NoContent();
    }
}
