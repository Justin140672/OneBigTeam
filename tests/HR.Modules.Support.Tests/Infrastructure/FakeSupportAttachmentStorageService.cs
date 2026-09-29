using System.Collections.Concurrent;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Support.Tests.Infrastructure;

internal sealed class FakeSupportAttachmentStorageService : ISupportAttachmentStorageService
{
    private readonly ConcurrentBag<UploadedFile> _uploads = new();
    private readonly ConcurrentDictionary<string, byte> _activeKeys = new();
    private readonly ConcurrentBag<string> _deleteAttempts = new();

    public IReadOnlyCollection<UploadedFile> Uploads => _uploads;

    public IReadOnlyCollection<string> ActiveKeys => _activeKeys.Keys.ToList();

    public IReadOnlyCollection<string> DeleteAttempts => _deleteAttempts;

    public HashSet<string> FailDeleteForKeys { get; } = [];

    public bool FailAllDeletes { get; set; }

    public HashSet<string> FailUploadForFileNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<string> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string storageFolder,
        CancellationToken cancellationToken)
    {
        if (FailUploadForFileNames.Contains(fileName))
            throw new InvalidOperationException($"Simulated upload failure for '{fileName}'.");

        var storageKey = $"{storageFolder}/{Guid.NewGuid():N}/{fileName}";
        _uploads.Add(new UploadedFile(storageKey, fileName, contentType, storageFolder));
        _activeKeys[storageKey] = 0;
        return Task.FromResult(storageKey);
    }

    public Task<Uri> GetDownloadUrlAsync(string storageKey, CancellationToken cancellationToken) =>
        Task.FromResult(new Uri($"https://example.test/{storageKey}"));

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        _deleteAttempts.Add(storageKey);

        if (FailAllDeletes || FailDeleteForKeys.Contains(storageKey))
            throw new InvalidOperationException($"Simulated delete failure for '{storageKey}'.");

        _activeKeys.TryRemove(storageKey, out _);
        return Task.CompletedTask;
    }

    public sealed record UploadedFile(string StorageKey, string FileName, string ContentType, string StorageFolder);
}
