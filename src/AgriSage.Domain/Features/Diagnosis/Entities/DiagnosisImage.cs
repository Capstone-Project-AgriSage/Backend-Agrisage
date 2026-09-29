using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Diagnosis.Entities;

// Image reference and metadata of a Diagnosis Case (bytes live in Object Storage). Changed only through
// DiagnosisCase, which keeps at most one active primary image.
public sealed class DiagnosisImage : SoftDeletableChildEntity
{
    private DiagnosisImage()
    {
    }

    internal DiagnosisImage(
        Guid diagnosisCaseId,
        string storageKey,
        string imageUrl,
        DateTimeOffset uploadedAt,
        string? fileName,
        string? mimeType,
        long? fileSizeBytes,
        int? width,
        int? height)
    {
        DiagnosisCaseId = diagnosisCaseId;
        StorageKey = Guard.NotNullOrWhiteSpace(storageKey);
        ImageUrl = Guard.NotNullOrWhiteSpace(imageUrl);
        UploadedAt = uploadedAt;
        FileName = fileName;
        MimeType = mimeType;
        FileSizeBytes = fileSizeBytes;
        Width = width;
        Height = height;
    }

    public Guid DiagnosisCaseId { get; private set; }

    public string StorageKey { get; private set; } = null!;

    public string ImageUrl { get; private set; } = null!;

    public string? FileName { get; private set; }

    public string? MimeType { get; private set; }

    public long? FileSizeBytes { get; private set; }

    public int? Width { get; private set; }

    public int? Height { get; private set; }

    public bool IsPrimary { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    internal void SetPrimary(bool isPrimary) => IsPrimary = isPrimary;
}
