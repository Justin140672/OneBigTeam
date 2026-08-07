using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.DownloadEmployeeDocument;

internal sealed class DownloadEmployeeDocumentHandler(
    DocumentsDbContext db,
    IDocumentStorageService storage,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<Uri>> HandleAsync(
        DownloadEmployeeDocumentRequest request,
        Guid downloadedBy,
        CancellationToken cancellationToken)
    {
        var row = await (
            from ed in db.EmployeeDocuments.AsNoTracking()
            join d  in db.Documents.AsNoTracking()     on ed.DocumentId    equals d.Id
            join dt in db.DocumentTypes.AsNoTracking() on d.DocumentTypeId equals dt.Id
            where ed.Id         == request.EmployeeDocumentId
               && ed.CompanyId  == request.CompanyId
               && ed.EmployeeId == request.EmployeeId
            select new
            {
                d.StorageKey,
                d.Title,
                d.FileName,
                DocumentTypeName = dt.Name,
                ed.Id,
                d.ScanStatus,
            }
        ).FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return Result.Failure<Uri>(Error.NotFound("Employee document was not found."));

        var scanError = ScanStatusAccessGuard.CheckDownloadable(row.ScanStatus);
        if (scanError is not null)
            return Result.Failure<Uri>(scanError);

        var url = await storage.GetDownloadUrlAsync(row.StorageKey, cancellationToken);

        await auditPublisher.PublishAsync(new DocumentDownloadedAuditEvent(
            request.CompanyId,
            request.EmployeeDocumentId,
            request.EmployeeId,
            row.Title,
            row.DocumentTypeName,
            row.FileName,
            downloadedBy,
            clock.UtcNowOffset()), cancellationToken);

        return Result.Success(url);
    }
}
