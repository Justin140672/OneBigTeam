namespace HR.Modules.Recruitment.Services;

internal interface ICandidateDocumentStorageService
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
    /// Opens the stored bytes for server-side processing (malware scanning) using the service's own
    /// credentials. Deliberately distinct from <see cref="GetDownloadUrlAsync"/>: scanning must never
    /// mint a signed URL for content that has not yet been proven clean.
    /// </summary>
    Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns a URL that can be used to download the file. Callers must only request one for a
    /// document whose malware scan is Clean (see CandidateDocument.IsDownloadable).
    /// </summary>
    Task<Uri> GetDownloadUrlAsync(
        string storageKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Permanently removes a file from storage.
    /// </summary>
    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken);
}
