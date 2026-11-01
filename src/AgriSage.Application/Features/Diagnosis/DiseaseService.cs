using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Diagnosis.Enums;
using Microsoft.EntityFrameworkCore;
using ValidationException = AgriSage.Application.Common.Exceptions.ValidationException;

namespace AgriSage.Application.Features.Diagnosis;

// Disease content and treatment guidance shown to Farmers after a verified diagnosis (AI_DIAGNOSIS.md section 10).
// The five diseases are seeded; their code and healthy flag never change. Treatments are deactivated, never removed.
public sealed class DiseaseService(IAgriSageDbContext context, AuditTrail? audit = null) : IDiseaseService
{
    public async Task<IReadOnlyList<DiseaseResponse>> ListAsync(bool? isActive, CancellationToken cancellationToken)
    {
        var query = context.Diseases.AsNoTracking().AsQueryable();
        if (isActive is { } active)
        {
            query = query.Where(d => d.IsActive == active);
        }

        var diseases = await query.OrderBy(d => d.Code).ToListAsync(cancellationToken);
        var ids = diseases.Select(d => d.Id).ToList();
        var treatments = await context.DiseaseTreatments.AsNoTracking()
            .Where(t => ids.Contains(t.DiseaseId)).ToListAsync(cancellationToken);
        var ingredients = await IngredientNamesAsync(treatments, cancellationToken);

        return diseases.Select(d => ToResponse(d, treatments.Where(t => t.DiseaseId == d.Id), ingredients)).ToList();
    }

    public async Task<DiseaseResponse> CreateAsync(DiseaseCreateRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim();
        if (await context.Diseases.IgnoreQueryFilters().AnyAsync(d => d.Code == code, cancellationToken))
        {
            throw new ConflictException($"A problem with code '{code}' already exists.");
        }

        var disease = new Disease(
            code,
            request.Name.Trim(),
            isHealthyClass: false,
            scientificName: Texts.Clean(request.ScientificName),
            description: Texts.Clean(request.Description),
            symptoms: Texts.Clean(request.Symptoms),
            causes: Texts.Clean(request.Causes),
            prevention: Texts.Clean(request.Prevention),
            problemType: EnumText.TryParse<ProblemType>(request.ProblemType, out var type) ? type : ProblemType.Disease,
            searchKeywords: request.SearchKeywords);
        context.Diseases.Add(disease);
        audit?.Record(
            "DISEASE_CREATED", "DISEASE", disease.Id, await ActiveStore.GetIdAsync(context, cancellationToken),
            newValues: new { disease.Code, disease.Name, ProblemType = EnumText.Format(disease.ProblemType) });
        await context.SaveChangesAsync(cancellationToken);

        return ToResponse(disease, [], new Dictionary<Guid, string>());
    }

    public async Task<DiseaseResponse> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var disease = await context.Diseases.FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new NotFoundException("Disease", id);

        if (active)
        {
            disease.Activate();
        }
        else
        {
            disease.Deactivate();
        }

        audit?.Record(
            active ? "DISEASE_ACTIVATED" : "DISEASE_DEACTIVATED", "DISEASE", disease.Id,
            await ActiveStore.GetIdAsync(context, cancellationToken), newValues: new { disease.Code, disease.IsActive });
        await context.SaveChangesAsync(cancellationToken);

        var treatments = await context.DiseaseTreatments.AsNoTracking()
            .Where(t => t.DiseaseId == id).ToListAsync(cancellationToken);

        return ToResponse(disease, treatments, await IngredientNamesAsync(treatments, cancellationToken));
    }

    public async Task<DiseaseResponse> UpdateAsync(Guid id, DiseaseUpdateRequest request, CancellationToken cancellationToken)
    {
        var disease = await context.Diseases.FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new NotFoundException("Disease", id);

        disease.UpdateContent(
            request.Name.Trim(),
            Texts.Clean(request.ScientificName),
            Texts.Clean(request.Description),
            Texts.Clean(request.Symptoms),
            Texts.Clean(request.Causes),
            Texts.Clean(request.Prevention));

        // Omitted keywords keep the stored value (older clients never send them); an empty text clears them.
        if (request.SearchKeywords is not null)
        {
            disease.UpdateKeywords(request.SearchKeywords);
        }

        await context.SaveChangesAsync(cancellationToken);

        var treatments = await context.DiseaseTreatments.AsNoTracking()
            .Where(t => t.DiseaseId == id).ToListAsync(cancellationToken);
        var ingredients = await IngredientNamesAsync(treatments, cancellationToken);

        return ToResponse(disease, treatments, ingredients);
    }

    public async Task<TreatmentItem> CreateTreatmentAsync(Guid diseaseId, TreatmentRequest request, CancellationToken cancellationToken)
    {
        if (!await context.Diseases.AsNoTracking().AnyAsync(d => d.Id == diseaseId, cancellationToken))
        {
            throw new NotFoundException("Disease", diseaseId);
        }

        var type = ParseType(request.TreatmentType);
        await EnsureIngredientAsync(request.ActiveIngredientId, cancellationToken);

        var treatment = new DiseaseTreatment(
            diseaseId, type, request.Title.Trim(), request.Instructions.Trim(),
            request.ActiveIngredientId, Texts.Clean(request.Precautions), request.Priority);
        context.DiseaseTreatments.Add(treatment);
        await context.SaveChangesAsync(cancellationToken);

        return await ItemAsync(treatment.Id, cancellationToken);
    }

    public async Task<TreatmentItem> UpdateTreatmentAsync(Guid id, TreatmentRequest request, CancellationToken cancellationToken)
    {
        var treatment = await context.DiseaseTreatments.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException("Disease treatment", id);

        var type = ParseType(request.TreatmentType);
        await EnsureIngredientAsync(request.ActiveIngredientId, cancellationToken);

        treatment.Update(
            type, request.Title.Trim(), request.Instructions.Trim(),
            request.ActiveIngredientId, Texts.Clean(request.Precautions), request.Priority);
        await context.SaveChangesAsync(cancellationToken);

        return await ItemAsync(id, cancellationToken);
    }

    public async Task<TreatmentItem> SetTreatmentActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var treatment = await context.DiseaseTreatments.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException("Disease treatment", id);

        if (active)
        {
            treatment.Activate();
        }
        else
        {
            treatment.Deactivate();
        }

        await context.SaveChangesAsync(cancellationToken);

        return await ItemAsync(id, cancellationToken);
    }

    private async Task<TreatmentItem> ItemAsync(Guid id, CancellationToken cancellationToken)
    {
        var treatment = await context.DiseaseTreatments.AsNoTracking().FirstAsync(t => t.Id == id, cancellationToken);
        var ingredients = await IngredientNamesAsync([treatment], cancellationToken);

        return ToItem(treatment, ingredients);
    }

    private async Task<Dictionary<Guid, string>> IngredientNamesAsync(
        IEnumerable<DiseaseTreatment> treatments, CancellationToken cancellationToken)
    {
        var ids = treatments.Where(t => t.ActiveIngredientId != null).Select(t => t.ActiveIngredientId!.Value).Distinct().ToList();

        return ids.Count == 0
            ? []
            : await context.ActiveIngredients.AsNoTracking().Where(i => ids.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.Name, cancellationToken);
    }

    private async Task EnsureIngredientAsync(Guid? ingredientId, CancellationToken cancellationToken)
    {
        // An inactive ingredient would never match a product (AI_DIAGNOSIS.md D13), so it is refused like an unknown one.
        if (ingredientId is { } id
            && !await context.ActiveIngredients.AsNoTracking().AnyAsync(i => i.Id == id && i.IsActive, cancellationToken))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["activeIngredientId"] = ["The active ingredient does not exist or is not active."]
            });
        }
    }

    private static TreatmentType ParseType(string text) =>
        EnumText.TryParse<TreatmentType>(text, out var type)
            ? type
            : throw new ValidationException(new Dictionary<string, string[]>
            {
                ["treatmentType"] = ["TreatmentType must be CULTURAL, CHEMICAL, PREVENTIVE or OTHER."]
            });

    private static DiseaseResponse ToResponse(
        Disease d, IEnumerable<DiseaseTreatment> treatments, IReadOnlyDictionary<Guid, string> ingredients) => new(
        d.Id, d.Code, d.Name, d.ScientificName, d.CropType, d.Description, d.Symptoms, d.Causes, d.Prevention,
        d.IsHealthyClass, d.IsActive,
        treatments.OrderByDescending(t => t.Priority).ThenBy(t => t.Title).Select(t => ToItem(t, ingredients)).ToList(),
        EnumText.Format(d.ProblemType), d.SearchKeywords);

    private static TreatmentItem ToItem(DiseaseTreatment t, IReadOnlyDictionary<Guid, string> ingredients) => new(
        t.Id, t.DiseaseId, EnumText.Format(t.TreatmentType), t.Title, t.Instructions, t.ActiveIngredientId,
        t.ActiveIngredientId is { } id && ingredients.TryGetValue(id, out var name) ? name : null,
        t.Precautions, t.Priority, t.IsActive);
}
