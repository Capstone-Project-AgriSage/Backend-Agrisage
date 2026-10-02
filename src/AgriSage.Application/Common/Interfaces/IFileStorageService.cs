namespace AgriSage.Application.Common.Interfaces;

// Object storage abstraction. The database stores the returned key/URL/metadata, never file bytes.
public interface IFileStorageService
{
    Task<StoredFileResult> UploadAsync(FileUploadRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(string storageKey, StorageArea area, CancellationToken cancellationToken);
}
