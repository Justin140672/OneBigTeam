using HR.Modules.DataImport.Services;

namespace HR.Modules.DataImport.Tests.Infrastructure;

internal sealed class FakeImportFileStorageService : IImportFileStorageService
{
    public List<(string FileName, string StorageKey)> Uploads { get; } = [];

    private readonly Dictionary<string, byte[]> _contentByStorageKey = new();

    public string GenerateStorageKey(string storageFolder, string fileName) =>
        $"{storageFolder}/{Guid.NewGuid():N}/{fileName}";

    public Task UploadAsync(
        Stream content,
        string storageKey,
        string contentType,
        CancellationToken cancellationToken)
    {
        var fileName = storageKey[(storageKey.LastIndexOf('/') + 1)..];
        Uploads.Add((fileName, storageKey));

        using var memoryStream = new MemoryStream();
        content.CopyTo(memoryStream);
        _contentByStorageKey[storageKey] = memoryStream.ToArray();

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken) =>
        Task.FromResult(_contentByStorageKey.ContainsKey(storageKey));

    public Task<Uri> GetDownloadUrlAsync(string storageKey, CancellationToken cancellationToken)
        => Task.FromResult(new Uri($"https://storage.example.com/{storageKey}"));

    public List<string> Deletions { get; } = [];

    public int ThrowOnNextDeleteAttempts { get; set; }

    public List<CancellationToken> DeleteCancellationTokens { get; } = [];

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        DeleteCancellationTokens.Add(cancellationToken);

        if (ThrowOnNextDeleteAttempts > 0)
        {
            ThrowOnNextDeleteAttempts--;
            throw new IOException($"Simulated transient failure deleting storage key '{storageKey}'.");
        }

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

    public void SeedContent(string storageKey, byte[] content)
    {
        _contentByStorageKey[storageKey] = content;
    }
}
