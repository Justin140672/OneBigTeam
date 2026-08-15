using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

/// <summary>
/// Aggregates real, already-persisted file-size data across every document-bearing table in the
/// Documents schema (employee documents, shared company documents + their historic versions, and
/// profile photos). This is the only genuine "storage usage" data available in the platform today —
/// there is no separate storage-accounting table, so usage is computed on demand from FileSize columns.
/// </summary>
internal sealed class DocumentStorageReader(DocumentsDbContext dbContext) : IDocumentStorageReader
{
    public async Task<DocumentStorageUsage> GetStorageUsageAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var documents = await dbContext.Documents
            .AsNoTracking()
            .Where(d => d.CompanyId == companyId)
            .Select(d => d.FileSize)
            .ToListAsync(cancellationToken);

        var sharedDocuments = await dbContext.SharedCompanyDocuments
            .AsNoTracking()
            .Where(d => d.CompanyId == companyId)
            .Select(d => d.FileSize)
            .ToListAsync(cancellationToken);

        var sharedDocumentVersions = await dbContext.SharedCompanyDocumentVersions
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .Select(v => v.FileSize)
            .ToListAsync(cancellationToken);

        var profilePhotos = await dbContext.EmployeeProfilePhotos
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .Select(p => p.FileSize)
            .ToListAsync(cancellationToken);

        var pendingProfilePhotos = await dbContext.PendingProfilePhotos
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .Select(p => p.FileSize)
            .ToListAsync(cancellationToken);

        var allSizes = documents
            .Concat(sharedDocuments)
            .Concat(sharedDocumentVersions)
            .Concat(profilePhotos)
            .Concat(pendingProfilePhotos)
            .ToList();

        return new DocumentStorageUsage(
            TotalStorageBytes: allSizes.Sum(),
            FileCount: allSizes.Count);
    }
}
