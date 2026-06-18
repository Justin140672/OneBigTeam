namespace HR.Modules.Documents.Services;

/// <summary>
/// Development implementation that stores files on the local file system.
/// Replace with a cloud implementation (Azure Blob, S3, etc.) for production.
/// </summary>
internal sealed class LocalDocumentStorageService : IDocumentStorageService
{
    private readonly string _basePath =
        Path.Combine(Path.GetTempPath(), "onebigteam", "documents");

    public async Task<string> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string storageFolder,
        CancellationToken cancellationToken)
    {
        var storageKey = $"{storageFolder.Trim('/')}/{Guid.NewGuid():N}/{fileName}";
        var fullPath   = ToFullPath(storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = File.Create(fullPath);
        await content.CopyToAsync(file, cancellationToken);

        return storageKey;
    }

    public Task<Uri> GetDownloadUrlAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        var fullPath = ToFullPath(storageKey);
        return Task.FromResult(new Uri(fullPath));
    }

    public Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        var fullPath = ToFullPath(storageKey);
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.CompletedTask;
    }

    private string ToFullPath(string storageKey) =>
        Path.Combine(_basePath, storageKey.Replace('/', Path.DirectorySeparatorChar));
}
