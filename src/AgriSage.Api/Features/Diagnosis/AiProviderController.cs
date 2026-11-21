using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Diagnosis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Diagnosis;

// The general vision models used for photo diagnosis and their API keys (AI_DIAGNOSIS.md 2.2): administrator only. One
// setting per provider (ANTHROPIC = Claude, GOOGLE = Gemini); an unknown provider is 404. The key is write-only: no response
// ever contains it, only whether one is stored and its last characters.
[ApiController]
[Route("api/admin/ai-providers")]
[Authorize(Roles = ApiRoles.Admin)]
public sealed class AiProviderController(IAiProviderSettingsService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AiProviderSettingsResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(cancellationToken));

    [HttpGet("{provider}")]
    public async Task<ActionResult<AiProviderSettingsResponse>> Get(string provider, CancellationToken cancellationToken) =>
        await service.GetAsync(provider, cancellationToken);

    [HttpPut("{provider}")]
    public async Task<ActionResult<AiProviderSettingsResponse>> Update(
        string provider, AiProviderSettingsRequest request, CancellationToken cancellationToken) =>
        await service.UpdateAsync(provider, request, cancellationToken);

    // Checks the stored key and model with the provider (no text generated, no cost).
    [HttpPost("{provider}/test")]
    public async Task<ActionResult<AiProviderTestResult>> Test(string provider, CancellationToken cancellationToken) =>
        await service.TestAsync(provider, cancellationToken);
}
