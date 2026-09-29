using HR.Modules.Recruitment.Services;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

internal sealed class FakeCandidateDocumentStorageService : ICandidateDocumentStorageService
{
    public List<(string FileName, string StorageKey)> Uploads { get; } = [];
    public List<string> Deletions { get; } = [];
    public HashSet<string> ExistingKeys { get; } = [];

    public Dictionary<string, byte[]> Contents { get; } = [];

    public int ThrowOnNextOpenReadAttempts { get; set; }

    public List<string> DownloadUrlRequests { get; } = [];

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        if (ThrowOnNextOpenReadAttempts > 0)
        {
            ThrowOnNextOpenReadAttempts--;
            throw new IOException($"Simulated storage failure reading '{storageKey}'.");
        }

        if (!Contents.TryGetValue(storageKey, out var bytes))
            throw new FileNotFoundException("Simulated missing blob.", storageKey);

        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

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

        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        Contents[storageKey] = buffer.ToArray();
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken) =>
        Task.FromResult(ExistingKeys.Contains(storageKey));

    public Task<Uri> GetDownloadUrlAsync(string storageKey, CancellationToken cancellationToken)
    {
        DownloadUrlRequests.Add(storageKey);
        return Task.FromResult(new Uri($"https://storage.example.com/{storageKey}"));
    }

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
        ExistingKeys.Remove(storageKey);
        Contents.Remove(storageKey);
        return Task.CompletedTask;
    }
}
