namespace HR.Modules.Documents.Services;

internal interface IDocumentStorageService
{
    /// <summary>
    /// Uploads a file and returns the storage key used to reference it.
    /// </summary>
    Task<string> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string storageFolder,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns a URL that can be used to download the file.
    /// </summary>
    Task<Uri> GetDownloadUrlAsync(
        string storageKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Opens a server-side read stream over the stored file, or null if the object does not exist.
    /// Used for embedding document binaries in the organisation data export.
    /// </summary>
    Task<Stream?> OpenReadStreamAsync(
        string storageKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Permanently removes a file from storage.
    /// </summary>
    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken);
}
