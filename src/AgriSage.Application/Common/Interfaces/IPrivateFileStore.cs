namespace AgriSage.Application.Common.Interfaces;

// Files in a private bucket (diagnosis photos): never reachable by a public URL. Kept apart from IFileStorageService so
// the public-bucket users and their test doubles do not change.
public interface IPrivateFileStore
{
    // A URL that works for `lifetime` and then expires; created at read time, never stored.
    Task<string> CreateSignedUrlAsync(string storageKey, StorageArea area, TimeSpan lifetime, CancellationToken cancellationToken);

    // The bytes of a stored file (the AI is run again on an existing photo).
    Task<byte[]> ReadAsync(string storageKey, StorageArea area, CancellationToken cancellationToken);
}
