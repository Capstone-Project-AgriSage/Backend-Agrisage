namespace AgriSage.Application.Common.Interfaces;

public sealed record FileUploadRequest(Stream Content, string FileName, string ContentType, string Folder);

public sealed record StoredFileResult(string StorageKey, string Url, long SizeBytes);
