using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Diagnosis.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Diagnosis;

// Which model answers a crop (AI_DIAGNOSIS.md D5, D19, version 2.4). Two kinds of model may be ACTIVE together:
// - a general model (framework ANTHROPIC or GOOGLE: Claude or Gemini), for every crop;
// - one crop model: a model of fixed classes served by the AI service (FastAPI). Its crop is the crop of its disease
//   labels (HEALTHY + DISEASE codes of one crop), e.g. the team's dragon fruit stem model.
// A crop is answered by the crop model of that crop when there is one, otherwise by the general model.
internal static class CropModels
{
    internal sealed record ActiveModel(AiModel Model, IReadOnlySet<string> Labels, string? CropType)
    {
        internal bool IsGeneral => AiFrameworks.IsGeneralModel(Model.Framework);
    }

    // The ACTIVE models with their labels and, for a crop model, its crop; newest first.
    internal static async Task<IReadOnlyList<ActiveModel>> ActiveAsync(
        IAgriSageDbContext context, CancellationToken cancellationToken)
    {
        var models = await context.AiModels.AsNoTracking()
            .Where(m => m.Status == AiModelStatus.Active)
            .OrderByDescending(m => m.DeployedAt).ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);
        if (models.Count == 0)
        {
            return [];
        }

        var labels = models.ToDictionary(m => m.Id, m => Labels(m.ClassLabels));
        var crops = await DiseaseCropsAsync(context, labels.Values.SelectMany(l => l), cancellationToken);

        return models
            .Select(m => new ActiveModel(m, labels[m.Id], AiFrameworks.IsGeneralModel(m.Framework) ? null : CropOf(labels[m.Id], crops)))
            .ToList();
    }

    // The model that answers this crop, or null when no ACTIVE model does.
    internal static ActiveModel? ForCrop(IReadOnlyList<ActiveModel> active, string cropType) =>
        active.FirstOrDefault(m => !m.IsGeneral && m.CropType == cropType)
        ?? active.FirstOrDefault(m => m.IsGeneral);

    // The crop of each non-healthy disease code among these labels (inactive diseases included: a model keeps its crop).
    internal static async Task<IReadOnlyDictionary<string, string>> DiseaseCropsAsync(
        IAgriSageDbContext context, IEnumerable<string> labels, CancellationToken cancellationToken)
    {
        var codes = labels.Distinct(StringComparer.Ordinal).ToList();
        if (codes.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        return await context.Diseases.AsNoTracking()
            .Where(d => !d.IsHealthyClass && codes.Contains(d.Code))
            .ToDictionaryAsync(d => d.Code, d => d.CropType, cancellationToken);
    }

    // The single crop of a crop model's disease labels; null when they name no crop or several.
    internal static string? CropOf(IEnumerable<string> labels, IReadOnlyDictionary<string, string> diseaseCrops)
    {
        var crops = labels.Where(diseaseCrops.ContainsKey).Select(l => diseaseCrops[l]).Distinct(StringComparer.Ordinal).ToList();

        return crops.Count == 1 ? crops[0] : null;
    }

    internal static IReadOnlySet<string> Labels(string classLabels)
    {
        try
        {
            return (JsonSerializer.Deserialize<List<string>>(classLabels) ?? []).ToHashSet(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
