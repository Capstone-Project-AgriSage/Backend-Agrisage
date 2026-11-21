using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Diagnosis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Diagnosis;

// The crops a photo can be sent for (AI_DIAGNOSIS.md 2.2). The Farmer picks one before the photo, so the model only chooses
// among that crop's diseases.
[ApiController]
public sealed class DiagnosisCropsController(ICropService service) : ControllerBase
{
    // Crops the active model can diagnose, with their disease names, for the Farmer's crop picker.
    [HttpGet("api/me/diagnosis/crops")]
    [Authorize(Roles = ApiRoles.Farmer)]
    public async Task<ActionResult<IReadOnlyList<CropResponse>>> Mine(CancellationToken cancellationToken) =>
        Ok(await service.ListForPhotoDiagnosisAsync(cancellationToken));

    // Every crop of the catalog, for the staff screens (disease form, model labels).
    [HttpGet("api/diagnosis/crops")]
    [Authorize(Roles = ApiRoles.Operate)]
    public async Task<ActionResult<IReadOnlyList<CropResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await service.ListAllAsync(cancellationToken));
}
