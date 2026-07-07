namespace HR.Modules.DataImport.Services;

internal interface IImportFileStorageService
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
    /// Permanently removes a file from storage.
    /// </summary>
    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Opens a readable stream for the file previously stored under the given key.
    /// </summary>
    Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken);
}
