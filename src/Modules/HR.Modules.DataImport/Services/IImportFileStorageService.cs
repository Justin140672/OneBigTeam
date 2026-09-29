namespace HR.Modules.DataImport.Services;

internal interface IImportFileStorageService
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

    Task<Uri> GetDownloadUrlAsync(
        string storageKey,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken);
}
