using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Diagnosis.Enums;
using AgriSage.Domain.Features.Products.Entities;

namespace AgriSage.Domain.Features.Diagnosis.Entities;

// Treatment guidance for a verified disease; chemical treatments may reference an Active Ingredient,
// cultural/preventive guidance may have none.
public sealed class DiseaseTreatment : SoftDeletableEntity
{
    private DiseaseTreatment()
    {
    }

    public DiseaseTreatment(
        Guid diseaseId,
        TreatmentType treatmentType,
        string title,
        string instructions,
        Guid? activeIngredientId = null,
        string? precautions = null,
        int priority = 0)
    {
        DiseaseId = diseaseId;
        Update(treatmentType, title, instructions, activeIngredientId, precautions, priority);
        IsActive = true;
    }

    public Guid DiseaseId { get; private set; }

    public Disease Disease { get; private set; } = null!;

    public Guid? ActiveIngredientId { get; private set; }

    public ActiveIngredient? ActiveIngredient { get; private set; }

    public TreatmentType TreatmentType { get; private set; }

    public string Title { get; private set; } = null!;

    public string Instructions { get; private set; } = null!;

    public string? Precautions { get; private set; }

    public int Priority { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(
        TreatmentType treatmentType,
        string title,
        string instructions,
        Guid? activeIngredientId,
        string? precautions,
        int priority)
    {
        TreatmentType = treatmentType;
        Title = Guard.NotNullOrWhiteSpace(title);
        Instructions = Guard.NotNullOrWhiteSpace(instructions);
        ActiveIngredientId = activeIngredientId;
        Precautions = precautions;
        Priority = priority;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
