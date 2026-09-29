using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Diagnosis.Enums;

namespace AgriSage.Domain.Features.Diagnosis.Entities;

// AI model version and deployment metadata. Only explicitly activated models are used for production inference.
// "class_labels contains the five approved Rice classes" is validated by Application (JSON format not fixed yet).
public sealed class AiModel : SoftDeletableEntity
{
    private AiModel()
    {
    }

    public AiModel(
        string name,
        string version,
        string framework,
        string modelStorageUrl,
        string classLabels,
        Guid createdBy,
        string? architecture = null,
        int? inputWidth = null,
        int? inputHeight = null,
        string? metrics = null)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Version = Guard.NotNullOrWhiteSpace(version);
        Framework = Guard.NotNullOrWhiteSpace(framework);
        ModelStorageUrl = Guard.NotNullOrWhiteSpace(modelStorageUrl);
        ClassLabels = Guard.NotNullOrWhiteSpace(classLabels);
        CreatedBy = createdBy;
        Architecture = architecture;
        InputWidth = inputWidth;
        InputHeight = inputHeight;
        Metrics = metrics;
        Status = AiModelStatus.Draft;
    }

    public string Name { get; private set; } = null!;

    public string Version { get; private set; } = null!;

    public string? Architecture { get; private set; }

    public string Framework { get; private set; } = null!;

    public string ModelStorageUrl { get; private set; } = null!;

    public int? InputWidth { get; private set; }

    public int? InputHeight { get; private set; }

    // Raw JSON (jsonb).
    public string ClassLabels { get; private set; } = null!;

    // Raw JSON (jsonb).
    public string? Metrics { get; private set; }

    public AiModelStatus Status { get; private set; }

    public DateTimeOffset? DeployedAt { get; private set; }

    public DateTimeOffset? RetiredAt { get; private set; }

    public Guid CreatedBy { get; private set; }

    public void Activate(DateTimeOffset deployedAt)
    {
        EnsureStatus(AiModelStatus.Draft);
        Status = AiModelStatus.Active;
        DeployedAt = deployedAt;
    }

    public void Retire(DateTimeOffset retiredAt)
    {
        EnsureStatus(AiModelStatus.Active);
        Status = AiModelStatus.Retired;
        RetiredAt = retiredAt;
    }

    private void EnsureStatus(AiModelStatus expected)
    {
        if (Status != expected)
        {
            throw new DomainException($"AI model '{Name} {Version}' is {Status}; expected {expected}.");
        }
    }
}
