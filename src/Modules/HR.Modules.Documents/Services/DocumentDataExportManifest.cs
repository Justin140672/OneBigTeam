using System.Globalization;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

/// <summary>
/// Story 2: contributes Documents-module metadata rows (documents, document types, employee-document
/// links) to the organisation data export, plus the list of stored files to embed in the ZIP under
/// documents/&lt;type&gt;/&lt;filename&gt;. company_id is enforced on every query and inside
/// <see cref="OpenDocumentAsync"/>.
///
/// <see cref="OpenDocumentAsync"/> verifies the storage key belongs to a document in the requested
/// company, then streams the bytes via <see cref="IDocumentStorageService.OpenReadStreamAsync"/>
/// (null when the object is missing). The build job tolerates null streams.
/// </summary>
internal sealed class DocumentDataExportManifest(DocumentsDbContext db, IDocumentStorageService storage)
    : IDocumentDataExportManifest
{
    public async Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var documents = await db.Documents.AsNoTracking()
            .Where(d => d.CompanyId == companyId)
            .Select(d => new { d.Id, d.EmployeeId, d.Title, d.Description, d.DocumentTypeId, d.FileName, d.FileSize, d.ContentType, d.Status, d.ExpiryDate, d.CreatedAt })
            .ToListAsync(cancellationToken);

        var documentsTable = new DataExportTable(
            "documents",
            ["Id", "EmployeeId", "Title", "Description", "DocumentTypeId", "FileName", "FileSize", "ContentType", "Status", "ExpiryDate", "CreatedAt"],
            documents.Select(d => (IReadOnlyList<string?>)new string?[]
            {
                d.Id.ToString(), d.EmployeeId?.ToString(), d.Title, d.Description, d.DocumentTypeId.ToString(),
                d.FileName, d.FileSize.ToString(CultureInfo.InvariantCulture), d.ContentType, d.Status.ToString(),
                d.ExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), d.CreatedAt.ToString("o", CultureInfo.InvariantCulture)
            }).ToList());

        var types = await db.DocumentTypes.AsNoTracking()
            .Where(t => t.CompanyId == companyId)
            .Select(t => new { t.Id, t.Name, t.Description, t.IsActive, t.AllowEmployeeUpload })
            .ToListAsync(cancellationToken);

        var typesTable = new DataExportTable(
            "document_types",
            ["Id", "Name", "Description", "IsActive", "AllowEmployeeUpload"],
            types.Select(t => (IReadOnlyList<string?>)new string?[]
            {
                t.Id.ToString(), t.Name, t.Description, t.IsActive ? "true" : "false", t.AllowEmployeeUpload ? "true" : "false"
            }).ToList());

        var links = await db.EmployeeDocuments.AsNoTracking()
            .Where(l => l.CompanyId == companyId)
            .Select(l => new { l.Id, l.EmployeeId, l.DocumentId, l.IssueDate, l.ExpiryDate, l.AcknowledgedAt, l.IsArchived, l.ArchivedAt, l.CreatedAt })
            .ToListAsync(cancellationToken);

        var linksTable = new DataExportTable(
            "employee_documents",
            ["Id", "EmployeeId", "DocumentId", "IssueDate", "ExpiryDate", "AcknowledgedAt", "IsArchived", "ArchivedAt", "CreatedAt"],
            links.Select(l => (IReadOnlyList<string?>)new string?[]
            {
                l.Id.ToString(), l.EmployeeId.ToString(), l.DocumentId.ToString(),
                l.IssueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                l.ExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                l.AcknowledgedAt?.ToString("o", CultureInfo.InvariantCulture),
                l.IsArchived ? "true" : "false",
                l.ArchivedAt?.ToString("o", CultureInfo.InvariantCulture),
                l.CreatedAt.ToString("o", CultureInfo.InvariantCulture)
            }).ToList());

        return [documentsTable, typesTable, linksTable];
    }

    public async Task<IReadOnlyList<DocumentExportFileEntry>> GetFileEntriesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var rows = await (
            from d in db.Documents.AsNoTracking()
            join t in db.DocumentTypes.AsNoTracking() on d.DocumentTypeId equals t.Id into types
            from t in types.DefaultIfEmpty()
            where d.CompanyId == companyId && d.StorageKey != ""
            select new { d.StorageKey, d.FileName, TypeName = t != null ? t.Name : "Uncategorised" })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new DocumentExportFileEntry(
                $"documents/{Sanitise(r.TypeName)}/{Sanitise(r.FileName)}",
                r.StorageKey))
            .ToList();
    }

    public async Task<Stream?> OpenDocumentAsync(Guid companyId, string storageKey, CancellationToken cancellationToken)
    {
        // Ownership check: never open a key that does not belong to this company.
        var belongs = await db.Documents.AsNoTracking()
            .AnyAsync(d => d.CompanyId == companyId && d.StorageKey == storageKey, cancellationToken);

        if (!belongs)
            return null;

        return await storage.OpenReadStreamAsync(storageKey, cancellationToken);
    }

    private static string Sanitise(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray();
        var cleaned = new string(chars).Trim();
        return string.IsNullOrEmpty(cleaned) ? "file" : cleaned;
    }
}
