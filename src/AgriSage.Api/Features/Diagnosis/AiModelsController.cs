using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Diagnosis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Diagnosis;

// AI model versions and their policies (AI_DIAGNOSIS.md section 5): Admin and Store Owner.
[ApiController]
[Authorize(Roles = ApiRoles.Manage)]
public sealed class AiModelsController(IAiModelService service) : ControllerBase
{
    [HttpGet("api/ai-models")]
    public async Task<ActionResult<PagedResult<AiModelResponse>>> List(
        [FromQuery] AiModelListRequest request, CancellationToken cancellationToken) =>
        await service.ListAsync(request, cancellationToken);

    [HttpGet("api/ai-models/{id:guid}")]
    public async Task<ActionResult<AiModelResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await service.GetAsync(id, cancellationToken);

    // Registers an exported model (its manifest.json). It starts as DRAFT and is not used until activated.
    [HttpPost("api/ai-models")]
    [ProducesResponseType<AiModelResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(AiModelRequest request, CancellationToken cancellationToken)
    {
        var response = await service.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpPost("api/ai-models/{id:guid}/activate")]
    public async Task<ActionResult<AiModelResponse>> Activate(Guid id, CancellationToken cancellationToken) =>
        await service.ActivateAsync(id, cancellationToken);

    [HttpPost("api/ai-models/{id:guid}/retire")]
    public async Task<ActionResult<AiModelResponse>> Retire(Guid id, CancellationToken cancellationToken) =>
        await service.RetireAsync(id, cancellationToken);

    [HttpGet("api/ai-models/{id:guid}/policies")]
    public async Task<ActionResult<IReadOnlyList<AiPolicyResponse>>> Policies(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.ListPoliciesAsync(id, cancellationToken));

    [HttpPost("api/ai-models/{id:guid}/policies")]
    [ProducesResponseType<AiPolicyResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreatePolicy(Guid id, AiPolicyRequest request, CancellationToken cancellationToken)
    {
        var response = await service.CreatePolicyAsync(id, request, cancellationToken);

        return CreatedAtAction(nameof(Policies), new { id }, response);
    }
}

[ApiController]
[Authorize(Roles = ApiRoles.Manage)]
public sealed class AiPoliciesController(IAiModelService service) : ControllerBase
{
    [HttpPost("api/ai-policies/{id:guid}/activate")]
    public async Task<ActionResult<AiPolicyResponse>> Activate(Guid id, CancellationToken cancellationToken) =>
        await service.ActivatePolicyAsync(id, cancellationToken);

    [HttpPost("api/ai-policies/{id:guid}/deactivate")]
    public async Task<ActionResult<AiPolicyResponse>> Deactivate(Guid id, CancellationToken cancellationToken) =>
        await service.DeactivatePolicyAsync(id, cancellationToken);
}
