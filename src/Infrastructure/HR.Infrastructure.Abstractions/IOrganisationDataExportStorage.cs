namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Dedicated private storage for organisation data export ZIP archives.
///
/// Follow-up D key convention: every build-job attempt uploads to its own object,
/// <c>organisation-exports/{companyId}/{exportId}/{attemptToken}.zip</c>, so concurrent or retried
/// attempts can never overwrite each other. Only the attempt that wins the ownership check has its
/// key published on the export row; the losing attempts' objects are swept up afterwards.
///
/// Supabase implementation for hosted environments, Local implementation for development/test — same
/// Local*/Supabase* pairing as ISupportAttachmentStorageService / IProfilePhotoStorageService.
/// DI-registered alongside the other storage services (InfrastructureModule / Program.cs), choosing
/// Local vs Supabase by configuration/environment.
/// </summary>
public interface IOrganisationDataExportStorage
{
    /// <summary>
    /// Uploads one build-job attempt's archive to its own per-attempt object key
    /// (<c>organisation-exports/{companyId}/{exportId}/{attemptToken}.zip</c>) and returns that key.
    /// </summary>
    Task<string> UploadAsync(Guid companyId, Guid exportId, Guid attemptToken, Stream content, CancellationToken cancellationToken);

    /// <summary>Opens the stored export archive for download, or null if the key is missing.</summary>
    Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>Permanently removes the export archive. Tolerates a missing blob.</summary>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>
    /// Follow-up D: lists every attempt object key currently stored under
    /// <c>organisation-exports/{companyId}/{exportId}/</c>. Used to sweep up abandoned attempt
    /// archives without touching the published one. Returns an empty list when nothing is stored.
    /// </summary>
    Task<IReadOnlyList<string>> ListAttemptKeysAsync(Guid companyId, Guid exportId, CancellationToken cancellationToken);
}
