using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.ExpireSharedCompanyDocument;

internal sealed class ExpireSharedCompanyDocumentHandler(
    DocumentsDbContext db,
    ITaskCanceller taskCanceller,
    IAuditEventPublisher auditPublisher,
    IClock clock)
{
    public async Task<Result<ExpireSharedCompanyDocumentResponse>> HandleAsync(
        ExpireSharedCompanyDocumentRequest request,
        Guid expiredBy,
        CancellationToken cancellationToken)
    {
        var document = await db.SharedCompanyDocuments
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.CompanyId == request.CompanyId, cancellationToken);

        if (document is null)
            return Result.Failure<ExpireSharedCompanyDocumentResponse>(
                Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        if (document.Status == SharedCompanyDocumentStatus.Expired)
            return Result.Failure<ExpireSharedCompanyDocumentResponse>(
                Error.Conflict("This document is already expired."));

        if (document.Status == SharedCompanyDocumentStatus.Archived)
            return Result.Failure<ExpireSharedCompanyDocumentResponse>(
                Error.Conflict("An archived document cannot be marked expired."));

        var now = clock.UtcNowOffset();
        document.MarkExpired(expiredBy, now);
        await db.SaveChangesAsync(cancellationToken);

        // An open "please review this" task makes no sense once the document is expired and
        // won't be renewed — same rationale/pattern as ArchiveSharedCompanyDocumentHandler
        // cancelling open Acknowledge tasks when archiving. No-op if nothing is open.
        var cancelledCount = await taskCanceller.CancelAllBySourceEntityAsync(
            request.CompanyId,
            document.Id,
            TaskSource.Document,
            TaskActionType.Review,
            cancellationToken);

        await auditPublisher.PublishAsync(new SharedCompanyDocumentExpiredAuditEvent(
            document.CompanyId,
            document.Id,
            document.Title,
            cancelledCount,
            expiredBy,
            now), cancellationToken);

        return Result.Success(new ExpireSharedCompanyDocumentResponse(
            document.Id,
            document.CompanyId,
            document.Status.ToString(),
            document.ExpiredBy!.Value,
            document.ExpiredAt!.Value,
            cancelledCount));
    }
}
