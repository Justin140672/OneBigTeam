using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.RestoreEmployeeDocument;

internal sealed class RestoreEmployeeDocumentHandler(
    DocumentsDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<RestoreEmployeeDocumentResponse>> HandleAsync(
        RestoreEmployeeDocumentRequest request,
        Guid restoredBy,
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
            return Result.Failure<RestoreEmployeeDocumentResponse>(
                Error.NotFound("Employee document was not found."));

        if (!row.ed.IsArchived)
            return Result.Failure<RestoreEmployeeDocumentResponse>(
                Error.Conflict("This document is not archived."));

        var now = clock.UtcNowOffset();
        row.ed.Restore(restoredBy, now);

        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(new EmployeeDocumentRestoredAuditEvent(
            request.CompanyId,
            row.ed.Id,
            request.EmployeeId,
            row.d.Title,
            row.DocumentTypeName,
            restoredBy,
            now), cancellationToken);

        return Result.Success(new RestoreEmployeeDocumentResponse(
            row.ed.Id,
            row.ed.CompanyId,
            restoredBy,
            now));
    }
}
