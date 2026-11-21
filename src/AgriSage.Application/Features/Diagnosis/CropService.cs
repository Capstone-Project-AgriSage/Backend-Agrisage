using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Diagnosis;
using AgriSage.Domain.Features.Diagnosis.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Diagnosis;

public sealed record CropDiseaseItem(string Code, string Name);

// PhotoDiagnosis: the ACTIVE model can name at least one disease of this crop, so a Farmer may send a photo of it.
public sealed record CropResponse(string Code, string Name, int DiseaseCount, bool PhotoDiagnosis, IReadOnlyList<CropDiseaseItem> Diseases);

public interface ICropService
{
    // Crops a Farmer can choose before sending a photo (AI_DIAGNOSIS.md 2.2): crops with an active disease the active model
    // can name. Before any model is active, every crop with diseases is listed (the case then fails and can be retried).
    Task<IReadOnlyList<CropResponse>> ListForPhotoDiagnosisAsync(CancellationToken cancellationToken);

    // Every crop of the catalog with its active diseases, for the staff screens (a crop with none is listed too).
    Task<IReadOnlyList<CropResponse>> ListAllAsync(CancellationToken cancellationToken);
}

public sealed class CropService(IAgriSageDbContext context) : ICropService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<CropResponse>> ListForPhotoDiagnosisAsync(CancellationToken cancellationToken)
    {
        var labels = await ActiveModelLabelsAsync(context, cancellationToken);
        var crops = await BuildAsync(labels, cancellationToken);

        return crops.Where(crop => crop.PhotoDiagnosis).ToList();
    }

    public async Task<IReadOnlyList<CropResponse>> ListAllAsync(CancellationToken cancellationToken) =>
        await BuildAsync(await ActiveModelLabelsAsync(context, cancellationToken), cancellationToken);

    private async Task<IReadOnlyList<CropResponse>> BuildAsync(IReadOnlySet<string>? modelLabels, CancellationToken cancellationToken)
    {
        var diseases = await context.Diseases.AsNoTracking()
            .Where(d => d.IsActive && !d.IsHealthyClass && d.ProblemType == ProblemType.Disease)
            .OrderBy(d => d.Name)
            .Select(d => new { d.Code, d.Name, d.CropType })
            .ToListAsync(cancellationToken);

        return CropCatalog.All.OrderBy(crop => crop.Order).Select(crop =>
        {
            var own = diseases.Where(d => d.CropType == crop.Code).ToList();
            var answerable = modelLabels is null ? own : own.Where(d => modelLabels.Contains(d.Code)).ToList();
            return new CropResponse(
                crop.Code, crop.Name, own.Count, answerable.Count > 0,
                own.Select(d => new CropDiseaseItem(d.Code, d.Name)).ToList());
        }).ToList();
    }

    // The class labels of the ACTIVE model; null when no model is active.
    internal static async Task<IReadOnlySet<string>?> ActiveModelLabelsAsync(IAgriSageDbContext context, CancellationToken cancellationToken)
    {
        var labels = await context.AiModels.AsNoTracking()
            .Where(m => m.Status == AiModelStatus.Active)
            .OrderByDescending(m => m.DeployedAt)
            .Select(m => m.ClassLabels)
            .FirstOrDefaultAsync(cancellationToken);
        if (labels is null)
        {
            return null;
        }

        try
        {
            return (JsonSerializer.Deserialize<List<string>>(labels, Json) ?? []).ToHashSet(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new HashSet<string>();
        }
    }
}
