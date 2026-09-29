namespace HR.Infrastructure.Abstractions;

public interface IDocumentDataExportManifest
{
    Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentExportFileEntry>> GetFileEntriesAsync(Guid companyId, CancellationToken cancellationToken);

    Task<Stream?> OpenDocumentAsync(Guid companyId, string storageKey, CancellationToken cancellationToken);
}

public sealed record DocumentExportFileEntry(string ZipPath, string StorageKey);
