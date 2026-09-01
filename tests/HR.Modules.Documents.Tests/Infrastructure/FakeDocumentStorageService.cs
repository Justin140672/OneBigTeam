using HR.Modules.Documents.Services;

namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class FakeDocumentStorageService : IDocumentStorageService
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

    /// <summary>Bytes returned by <see cref="OpenReadStreamAsync"/>, keyed by storage key. Missing key => null.</summary>
    public Dictionary<string, byte[]> Contents { get; } = [];

    public Task<Stream?> OpenReadStreamAsync(string storageKey, CancellationToken cancellationToken)
        => Task.FromResult(Contents.TryGetValue(storageKey, out var bytes)
            ? (Stream)new MemoryStream(bytes, writable: false)
            : null);

    public List<string> Deletions { get; } = [];
    public bool ThrowOnDelete { get; set; }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        if (ThrowOnDelete)
            throw new InvalidOperationException("Simulated storage delete failure.");

        Deletions.Add(storageKey);
        return Task.CompletedTask;
    }
}
