using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Diagnosis.Entities;

// Rice disease/health class and its knowledge content (LEAF_BLAST, ..., HEALTHY).
// "Exactly one Healthy class for the production model" is enforced by Application/seed data.
public sealed class Disease : SoftDeletableEntity
{
    public const string RiceCropType = "RICE";

    private Disease()
    {
    }

    public Disease(
        string code,
        string name,
        bool isHealthyClass = false,
        string cropType = RiceCropType,
        string? scientificName = null,
        string? description = null,
        string? symptoms = null,
        string? causes = null,
        string? prevention = null)
    {
        Code = Guard.NotNullOrWhiteSpace(code);
        CropType = Guard.NotNullOrWhiteSpace(cropType);
        IsHealthyClass = isHealthyClass;
        UpdateContent(name, scientificName, description, symptoms, causes, prevention);
        IsActive = true;
    }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? ScientificName { get; private set; }

    public string CropType { get; private set; } = null!;

    public string? Description { get; private set; }

    public string? Symptoms { get; private set; }

    public string? Causes { get; private set; }

    public string? Prevention { get; private set; }

    public bool IsHealthyClass { get; private set; }

    public bool IsActive { get; private set; }

    public void UpdateContent(
        string name,
        string? scientificName,
        string? description,
        string? symptoms,
        string? causes,
        string? prevention)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        ScientificName = scientificName;
        Description = description;
        Symptoms = symptoms;
        Causes = causes;
        Prevention = prevention;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
