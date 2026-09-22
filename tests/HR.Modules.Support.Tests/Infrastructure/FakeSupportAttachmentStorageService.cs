using System.Collections.Concurrent;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Support.Tests.Infrastructure;

/// <summary>
/// Reliability review issue 4 (P1): the previous version of this fake did not record
/// <see cref="DeleteAsync"/> calls at all, so a cleanup-on-failure assertion could pass even when
/// no cleanup ever happened — a broken test. This version tracks every key that is currently
/// "active" in fake storage (uploaded but not yet deleted) plus every delete attempt, and can be
/// configured to simulate a delete failure for specific keys so retry/reconciliation behaviour is
/// testable too.
/// </summary>
internal sealed class FakeSupportAttachmentStorageService : ISupportAttachmentStorageService
{
    private readonly ConcurrentBag<UploadedFile> _uploads = new();
    private readonly ConcurrentDictionary<string, byte> _activeKeys = new();
    private readonly ConcurrentBag<string> _deleteAttempts = new();

    public IReadOnlyCollection<UploadedFile> Uploads => _uploads;

    /// <summary>Storage keys currently believed to exist in the fake store (uploaded, not yet successfully deleted).</summary>
    public IReadOnlyCollection<string> ActiveKeys => _activeKeys.Keys.ToList();

    /// <summary>Every key that <see cref="DeleteAsync"/> was called for, successful or not.</summary>
    public IReadOnlyCollection<string> DeleteAttempts => _deleteAttempts;

    /// <summary>Keys for which <see cref="DeleteAsync"/> should throw, simulating a storage-provider failure.</summary>
    public HashSet<string> FailDeleteForKeys { get; } = [];

    /// <summary>Security review finding #4 (P1): when a test doesn't know a storage key in advance
    /// (it's a random GUID minted inside <see cref="UploadAsync"/>), this makes every
    /// <see cref="DeleteAsync"/> call fail regardless of key, so the compensating-delete-also-fails
    /// path can be exercised deterministically.</summary>
    public bool FailAllDeletes { get; set; }

    /// <summary>Reliability review issue 4 (P1): file names for which <see cref="UploadAsync"/>
    /// should throw, simulating a transport/provider error partway through a multi-file batch.</summary>
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
