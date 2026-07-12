namespace HR.Infrastructure.Abstractions;

public interface IProfilePhotoStorageService
{
    /// <summary>
    /// Uploads a profile photo and returns the storage key used to reference it.
    /// </summary>
    Task<string> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string storageFolder,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns a URL that can be used to download the profile photo.
    /// </summary>
    Task<Uri> GetDownloadUrlAsync(
        string storageKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Permanently removes a profile photo from storage.
    /// </summary>
    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken);
}
