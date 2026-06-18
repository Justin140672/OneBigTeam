using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.DeleteEmployeeDocument;

internal sealed class DeleteEmployeeDocumentHandler(
    DocumentsDbContext db,
    IDocumentStorageService storage,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result> HandleAsync(
        DeleteEmployeeDocumentRequest request,
        Guid deletedBy,
        CancellationToken cancellationToken)
    {
        var row = await (
            from ed in db.EmployeeDocuments
            join d  in db.Documents     on ed.DocumentId    equals d.Id
            join dt in db.DocumentTypes on d.DocumentTypeId equals dt.Id
            where ed.Id        == request.EmployeeDocumentId
               && ed.CompanyId == request.CompanyId
               && ed.EmployeeId == request.EmployeeId
            select new { ed, d, DocumentTypeName = dt.Name })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
            return Result.Failure(Error.NotFound("Employee document was not found."));

        db.EmployeeDocuments.Remove(row.ed);

        var otherLinks = await db.EmployeeDocuments
            .AnyAsync(
                ed => ed.DocumentId == row.d.Id &&
                      ed.Id         != row.ed.Id,
                cancellationToken);

        string? storageKeyToDelete = null;

        if (!otherLinks)
        {
            storageKeyToDelete = row.d.StorageKey;
            db.Documents.Remove(row.d);
        }

        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(new DocumentDeletedAuditEvent(
            request.CompanyId,
            row.ed.Id,
            request.EmployeeId,
            row.d.Title,
            row.DocumentTypeName,
            row.d.FileName,
            row.d.FileSize,
            row.ed.IssueDate,
            row.ed.ExpiryDate,
            deletedBy,
            clock.UtcNowOffset()), cancellationToken);

        if (storageKeyToDelete is not null)
            await storage.DeleteAsync(storageKeyToDelete, cancellationToken);

        return Result.Success();
    }
}
