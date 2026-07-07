using HR.Modules.DataImport.Services;

namespace HR.Modules.DataImport.Tests.Infrastructure;

internal sealed class FakeImportFileStorageService : IImportFileStorageService
{
    public List<(string FileName, string StorageKey)> Uploads { get; } = [];

    public Task<string> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string storageFolder,
        CancellationToken cancellationToken)
    {
        var storageKey = $"{storageFolder}/{Guid.NewGuid():N}/{fileName}";
        Uploads.Add((fileName, storageKey));
        return Task.FromResult(storageKey);
    }

    public Task<Uri> GetDownloadUrlAsync(string storageKey, CancellationToken cancellationToken)
        => Task.FromResult(new Uri($"https://storage.example.com/{storageKey}"));

    public List<string> Deletions { get; } = [];

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        Deletions.Add(storageKey);
        return Task.CompletedTask;
    }
}
