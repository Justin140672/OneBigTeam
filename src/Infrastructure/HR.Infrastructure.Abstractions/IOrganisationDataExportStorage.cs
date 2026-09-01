namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Dedicated private storage for organisation data export ZIP archives. Key convention:
/// organisation-exports/{companyId}/{exportId}.zip. Supabase implementation for hosted
/// environments, Local implementation for development/test — same Local*/Supabase* pairing as
/// ISupportAttachmentStorageService / IProfilePhotoStorageService. DI-registered alongside the
/// other storage services (InfrastructureModule / Program.cs), choosing Local vs Supabase by
/// configuration/environment.
/// </summary>
public interface IOrganisationDataExportStorage
{
    /// <summary>Uploads the export archive and returns the storage key.</summary>
    Task<string> UploadAsync(Guid companyId, Guid exportId, Stream content, CancellationToken cancellationToken);

    /// <summary>Opens the stored export archive for download, or null if the key is missing.</summary>
    Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>Permanently removes the export archive. Tolerates a missing blob.</summary>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}
