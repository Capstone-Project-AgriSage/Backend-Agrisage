using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Diagnosis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Diagnosis;

// Store side of AI diagnosis (AI_DIAGNOSIS.md 2.0, section 8): read only, to follow up with farmers. Human Review and its
// routes (start-review, review, rerun-ai, recommendations) were removed on 2026-10-21.
[ApiController]
[Route("api/diagnosis-cases")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class DiagnosisCasesController(IDiagnosisCaseService service) : ControllerBase
{
    // Newest first.
    [HttpGet]
    public async Task<ActionResult<PagedResult<DiagnosisCaseListItem>>> List(
        [FromQuery] DiagnosisCaseListRequest request, CancellationToken cancellationToken) =>
        await service.ListAsync(request, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DiagnosisCaseResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await service.GetAsync(id, cancellationToken);
}
