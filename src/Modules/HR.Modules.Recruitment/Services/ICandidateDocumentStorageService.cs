namespace HR.Modules.Recruitment.Services;

internal interface ICandidateDocumentStorageService
{
    string GenerateStorageKey(string storageFolder, string fileName);

    Task UploadAsync(
        Stream content,
        string storageKey,
        string contentType,
        CancellationToken cancellationToken);

    Task<bool> ExistsAsync(
        string storageKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Opens the stored bytes for server-side processing (malware scanning) using the service's own
    /// credentials. Deliberately distinct from <see cref="GetDownloadUrlAsync"/>: scanning must never
    /// mint a signed URL for content that has not yet been proven clean.
    /// </summary>
    Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken);

    Task<Uri> GetDownloadUrlAsync(
        string storageKey,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken);
}
