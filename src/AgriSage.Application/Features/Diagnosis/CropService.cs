using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Diagnosis;
using AgriSage.Domain.Features.Diagnosis.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Diagnosis;

public sealed record CropDiseaseItem(string Code, string Name);

// DiseaseCount: every active disease of the crop. Diseases: only those the model answering the crop (D19) can name (what the
// Farmer's app lists as "AI nhận diện được"; all of them while no model is active). PhotoDiagnosis: at least one, so a photo
// may be sent.
public sealed record CropResponse(string Code, string Name, int DiseaseCount, bool PhotoDiagnosis, IReadOnlyList<CropDiseaseItem> Diseases);

public interface ICropService
{
    // Crops a Farmer can choose before sending a photo (AI_DIAGNOSIS.md 2.2, 2.4): crops with an active disease the model
    // answering the crop (its crop model, else the general model) can name. Before any model is active, every crop with
    // diseases is listed (the case then fails and can be retried).
    Task<IReadOnlyList<CropResponse>> ListForPhotoDiagnosisAsync(CancellationToken cancellationToken);

    // Every crop of the catalog with its active diseases, for the staff screens (a crop with none is listed too).
    Task<IReadOnlyList<CropResponse>> ListAllAsync(CancellationToken cancellationToken);
}

public sealed class CropService(IAgriSageDbContext context) : ICropService
{
    public async Task<IReadOnlyList<CropResponse>> ListForPhotoDiagnosisAsync(CancellationToken cancellationToken)
    {
        var crops = await BuildAsync(cancellationToken);

        return crops.Where(crop => crop.PhotoDiagnosis).ToList();
    }

    public Task<IReadOnlyList<CropResponse>> ListAllAsync(CancellationToken cancellationToken) => BuildAsync(cancellationToken);

    private async Task<IReadOnlyList<CropResponse>> BuildAsync(CancellationToken cancellationToken)
    {
        var active = await CropModels.ActiveAsync(context, cancellationToken);
        var diseases = await context.Diseases.AsNoTracking()
            .Where(d => d.IsActive && !d.IsHealthyClass && d.ProblemType == ProblemType.Disease)
            .OrderBy(d => d.Name)
            .Select(d => new { d.Code, d.Name, d.CropType })
            .ToListAsync(cancellationToken);

        return CropCatalog.All.OrderBy(crop => crop.Order).Select(crop =>
        {
            var own = diseases.Where(d => d.CropType == crop.Code).ToList();
            // No model active at all: every crop with diseases is listed (the case then fails and can be retried). Otherwise
            // a crop is answered by its own crop model or the general model (D19), and only the diseases that model names count.
            var answerable = active.Count == 0
                ? own
                : CropModels.ForCrop(active, crop.Code) is { } model ? own.Where(d => model.Labels.Contains(d.Code)).ToList() : [];
            return new CropResponse(
                crop.Code, crop.Name, own.Count, answerable.Count > 0,
                answerable.Select(d => new CropDiseaseItem(d.Code, d.Name)).ToList());
        }).ToList();
    }
}
