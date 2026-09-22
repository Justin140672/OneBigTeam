namespace HR.Modules.DataImport.Services;

internal interface IImportFileStorageService
{
    /// <summary>
    /// Reserves the final storage key for an upload before any bytes are written. Deterministic
    /// given its inputs plus a fresh random component (never derived only after a successful
    /// upload) so callers can persist a durable "upload intent" record naming this exact key prior
    /// to calling <see cref="UploadAsync(Stream,string,string,CancellationToken)"/>.
    /// </summary>
    string GenerateStorageKey(string storageFolder, string fileName);

    /// <summary>
    /// Uploads a file to the given, previously reserved storage key (see
    /// <see cref="GenerateStorageKey"/>).
    /// </summary>
    Task UploadAsync(
        Stream content,
        string storageKey,
        string contentType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns whether an object currently exists at the given storage key. Used by reconciliation
    /// to distinguish a blob that was actually uploaded (and must be deleted) from one whose upload
    /// never completed (nothing to delete, the durable intent can simply be cleared).
    /// </summary>
    Task<bool> ExistsAsync(
        string storageKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns a URL that can be used to download the file.
    /// </summary>
    Task<Uri> GetDownloadUrlAsync(
        string storageKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Permanently removes a file from storage. Must be idempotent: deleting a key that is
    /// already gone (already deleted by a previous attempt, or never existed) must succeed rather
    /// than throw, so retention retry/sweep logic can safely call this more than once for the
    /// same storage key.
    /// </summary>
    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Opens a readable stream for the file previously stored under the given key.
    /// </summary>
    Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken);
}
