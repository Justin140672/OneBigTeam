using System.Collections.Concurrent;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Support.Tests.Infrastructure;

internal sealed class FakeSupportAttachmentStorageService : ISupportAttachmentStorageService
{
    private readonly ConcurrentBag<UploadedFile> _uploads = new();

    public IReadOnlyCollection<UploadedFile> Uploads => _uploads;

    public Task<string> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string storageFolder,
        CancellationToken cancellationToken)
    {
        var storageKey = $"{storageFolder}/{fileName}";
        _uploads.Add(new UploadedFile(storageKey, fileName, contentType, storageFolder));
        return Task.FromResult(storageKey);
    }

    public Task<Uri> GetDownloadUrlAsync(string storageKey, CancellationToken cancellationToken) =>
        Task.FromResult(new Uri($"https://example.test/{storageKey}"));

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) => Task.CompletedTask;

    public sealed record UploadedFile(string StorageKey, string FileName, string ContentType, string StorageFolder);
}
