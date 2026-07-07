using HR.Modules.DataImport.Services;

namespace HR.Modules.DataImport.Tests.Infrastructure;

internal sealed class FakeImportFileStorageService : IImportFileStorageService
{
    public List<(string FileName, string StorageKey)> Uploads { get; } = [];

    private readonly Dictionary<string, byte[]> _contentByStorageKey = new();

    public Task<string> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string storageFolder,
        CancellationToken cancellationToken)
    {
        var storageKey = $"{storageFolder}/{Guid.NewGuid():N}/{fileName}";
        Uploads.Add((fileName, storageKey));

        using var memoryStream = new MemoryStream();
        content.CopyTo(memoryStream);
        _contentByStorageKey[storageKey] = memoryStream.ToArray();

        return Task.FromResult(storageKey);
    }

    public Task<Uri> GetDownloadUrlAsync(string storageKey, CancellationToken cancellationToken)
        => Task.FromResult(new Uri($"https://storage.example.com/{storageKey}"));

    public List<string> Deletions { get; } = [];

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        Deletions.Add(storageKey);
        _contentByStorageKey.Remove(storageKey);
        return Task.CompletedTask;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        if (!_contentByStorageKey.TryGetValue(storageKey, out var bytes))
            throw new FileNotFoundException($"No fake content stored for key '{storageKey}'.");

        Stream stream = new MemoryStream(bytes);
        return Task.FromResult(stream);
    }

    /// <summary>
    /// Test helper: seeds content for a storage key without going through UploadAsync
    /// (e.g. when a session was created directly rather than via the upload endpoint).
    /// </summary>
    public void SeedContent(string storageKey, byte[] content)
    {
        _contentByStorageKey[storageKey] = content;
    }
}
