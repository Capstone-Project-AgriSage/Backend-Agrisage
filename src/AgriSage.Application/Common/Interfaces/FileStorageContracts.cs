namespace AgriSage.Application.Common.Interfaces;

// Which storage bucket a file belongs to (the provider adapter maps it to a bucket name).
public enum StorageArea
{
    ProductImages,
    DeliveryProofs,

    // Private bucket: photos a Farmer sends for AI diagnosis. Read only through IPrivateFileStore (signed URLs).
    DiagnosisImages
}

public sealed record FileUploadRequest(
    Stream Content, string FileName, string ContentType, string Folder, StorageArea Area = StorageArea.ProductImages);

public sealed record StoredFileResult(string StorageKey, string Url, long SizeBytes);
