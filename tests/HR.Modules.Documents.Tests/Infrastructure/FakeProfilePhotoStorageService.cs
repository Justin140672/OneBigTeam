using HR.Infrastructure.Abstractions;

namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class FakeProfilePhotoStorageService : IProfilePhotoStorageService
{
    public List<(string FileName, string StorageKey)> Uploads { get; } = [];
    public List<string> Deletions { get; } = [];
    public List<string> DownloadUrlRequests { get; } = [];
    public List<string> OpenedForRead { get; } = [];
    public List<(string From, string To)> Promotions { get; } = [];
    public bool ThrowOnDelete { get; set; }
    public bool ThrowOnPromote { get; set; }
    public bool ThrowOnRead { get; set; }
    public byte[] ContentToRead { get; set; } = [1, 2, 3];

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
    {
        DownloadUrlRequests.Add(storageKey);
        return Task.FromResult(new Uri($"https://storage.example.com/{storageKey}"));
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        if (ThrowOnRead)
            throw new InvalidOperationException("Simulated storage read failure.");

        OpenedForRead.Add(storageKey);
        return Task.FromResult<Stream>(new MemoryStream(ContentToRead));
    }

    public Task<string> PromoteToCleanAsync(string quarantineStorageKey, CancellationToken cancellationToken)
    {
        if (ThrowOnPromote)
            throw new InvalidOperationException("Simulated storage promote failure.");

        var cleanKey = ProfilePhotoStorageKeys.ToCleanKey(quarantineStorageKey);
        Promotions.Add((quarantineStorageKey, cleanKey));
        return Task.FromResult(cleanKey);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        if (ThrowOnDelete)
            throw new InvalidOperationException("Simulated storage delete failure.");

        Deletions.Add(storageKey);
        return Task.CompletedTask;
    }
}
