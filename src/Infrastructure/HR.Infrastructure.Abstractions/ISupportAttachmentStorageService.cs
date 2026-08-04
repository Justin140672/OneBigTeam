namespace HR.Infrastructure.Abstractions;

public interface ISupportAttachmentStorageService
{
    /// <summary>
    /// Uploads a support request/response attachment (e.g. screenshot) and returns the storage key
    /// used to reference it.
    /// </summary>
    Task<string> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string storageFolder,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns a URL that can be used to download the attachment.
    /// </summary>
    Task<Uri> GetDownloadUrlAsync(
        string storageKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Permanently removes an attachment from storage.
    /// </summary>
    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken);
}
