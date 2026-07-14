using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.ArchiveSharedCompanyDocument;

internal sealed class ArchiveSharedCompanyDocumentHandler(
    DocumentsDbContext db,
    ITaskCanceller taskCanceller,
    IAuditEventPublisher auditPublisher,
    IClock clock)
{
    public async Task<Result<ArchiveSharedCompanyDocumentResponse>> HandleAsync(
        ArchiveSharedCompanyDocumentRequest request,
        Guid archivedBy,
        CancellationToken cancellationToken)
    {
        var document = await db.SharedCompanyDocuments
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.CompanyId == request.CompanyId, cancellationToken);

        if (document is null)
            return Result.Failure<ArchiveSharedCompanyDocumentResponse>(
                Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        if (document.Status == SharedCompanyDocumentStatus.Archived)
            return Result.Failure<ArchiveSharedCompanyDocumentResponse>(
                Error.Conflict("This document is already archived."));

        var now = clock.UtcNowOffset();
        var reason = request.Reason.Trim();
        document.Archive(archivedBy, reason, now);
        await db.SaveChangesAsync(cancellationToken);

        // Cancelling is safe/correct regardless of RequiresAcknowledgement's current value — a
        // document could have had acknowledgement required at some point with tasks still
        // outstanding, even if the setting was later toggled off. No-op if nothing is open.
        var cancelledCount = await taskCanceller.CancelAllBySourceEntityAsync(
            request.CompanyId,
            document.Id,
            TaskSource.Document,
            TaskActionType.Acknowledge,
            cancellationToken);

        await auditPublisher.PublishAsync(new SharedCompanyDocumentArchivedAuditEvent(
            document.CompanyId,
            document.Id,
            document.Title,
            reason,
            cancelledCount,
            archivedBy,
            now), cancellationToken);

        return Result.Success(new ArchiveSharedCompanyDocumentResponse(
            document.Id,
            document.CompanyId,
            document.Status.ToString(),
            document.ArchivedBy!.Value,
            document.ArchivedAt!.Value,
            document.ArchiveReason!,
            cancelledCount));
    }
}
