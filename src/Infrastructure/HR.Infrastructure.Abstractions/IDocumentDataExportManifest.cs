namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Cross-module read surface for the organisation data export job to obtain Documents-module
/// metadata rows plus a way to stream each stored file into the export ZIP under
/// documents/&lt;category&gt;/&lt;filename&gt; in its original format. Implemented by an internal service in
/// HR.Modules.Documents (which reuses IDocumentStorageService internally), DI-registered in
/// DocumentsModule. Must enforce company_id — OpenDocumentAsync returns null if the storage key
/// does not belong to the given company.
/// </summary>
public interface IDocumentDataExportManifest
{
    Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken);

    /// <summary>Files to embed in the ZIP: relative ZIP path (e.g. "documents/Contracts/offer.pdf") + storage key.</summary>
    Task<IReadOnlyList<DocumentExportFileEntry>> GetFileEntriesAsync(Guid companyId, CancellationToken cancellationToken);

    Task<Stream?> OpenDocumentAsync(Guid companyId, string storageKey, CancellationToken cancellationToken);
}

public sealed record DocumentExportFileEntry(string ZipPath, string StorageKey);
