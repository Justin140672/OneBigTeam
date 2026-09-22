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

    /// <summary>
    /// Test helper: when greater than zero, the next N calls to <see cref="DeleteAsync"/> throw
    /// (simulating a transient storage failure) instead of succeeding, decrementing by one per
    /// call. Used to exercise the retry/failed-attempt paths in ValidateImportSessionHandler and
    /// PurgeImportSessionFilesJob.
    /// </summary>
    public int ThrowOnNextDeleteAttempts { get; set; }

    /// <summary>Test helper: records the CancellationToken passed to each DeleteAsync call, so
    /// tests can assert compensation used an independent cleanup token rather than a cancelled
    /// request token.</summary>
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

    /// <summary>
    /// Test helper: seeds content for a storage key without going through UploadAsync
    /// (e.g. when a session was created directly rather than via the upload endpoint).
    /// </summary>
    public void SeedContent(string storageKey, byte[] content)
    {
        _contentByStorageKey[storageKey] = content;
    }
}
