using HR.Modules.Recruitment.Services;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

internal sealed class FakeCandidateDocumentStorageService : ICandidateDocumentStorageService
{
    public List<(string FileName, string StorageKey)> Uploads { get; } = [];
    public List<string> Deletions { get; } = [];
    public HashSet<string> ExistingKeys { get; } = [];

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
        ExistingKeys.Add(storageKey);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken) =>
        Task.FromResult(ExistingKeys.Contains(storageKey));

    public Task<Uri> GetDownloadUrlAsync(string storageKey, CancellationToken cancellationToken)
        => Task.FromResult(new Uri($"https://storage.example.com/{storageKey}"));

    /// <summary>
    /// Test helper: when greater than zero, the next N calls to <see cref="DeleteAsync"/> throw
    /// (simulating a transient storage failure) instead of succeeding, decrementing by one per
    /// call. Mirrors FakeImportFileStorageService's helper of the same name/shape.
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
        ExistingKeys.Remove(storageKey);
        return Task.CompletedTask;
    }
}
