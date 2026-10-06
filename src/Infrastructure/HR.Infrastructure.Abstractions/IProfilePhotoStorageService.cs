namespace HR.Infrastructure.Abstractions;

public interface IProfilePhotoStorageService
{
    Task<string> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string storageFolder,
        CancellationToken cancellationToken);

    /// <summary>
    /// Mints a time-limited download URL. Callers must only invoke this after verifying the
    /// persisted scan status is Clean; the URL itself bypasses application-level scan checks.
    /// </summary>
    Task<Uri> GetDownloadUrlAsync(
        string storageKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Opens the object using internal storage credentials (never an externally usable signed URL).
    /// Used by the malware scanner. The caller owns and must dispose the returned stream.
    /// </summary>
    Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Copies a quarantined object to a new, unguessable clean key and returns it. The quarantine
    /// object is left in place so the caller can commit the new key before deleting the source.
    /// Keys outside quarantine are returned unchanged.
    /// </summary>
    Task<string> PromoteToCleanAsync(
        string quarantineStorageKey,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken);
}
