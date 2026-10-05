namespace AgriSage.Application.Common.Interfaces;

// Object storage abstraction. The database stores the returned key/URL/metadata, never file bytes.
public interface IFileStorageService
{
    Task<StoredFileResult> UploadAsync(FileUploadRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(string storageKey, StorageArea area, CancellationToken cancellationToken);

    // The storage key when the URL is a public URL of the area's bucket (as returned by UploadAsync), otherwise null.
    // Used to accept only this store's own uploaded photos in other requests (e.g. delivery proofs, FLOW_2 §8).
    string? KeyFromPublicUrl(string url, StorageArea area);
}
