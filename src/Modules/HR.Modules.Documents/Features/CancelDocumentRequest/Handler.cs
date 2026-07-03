using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.CancelDocumentRequest;

internal sealed class CancelDocumentRequestHandler(
    DocumentsDbContext db,
    ITaskCanceller taskCanceller,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result> HandleAsync(
        CancelDocumentRequestRequest request,
        Guid cancelledBy,
        CancellationToken cancellationToken)
    {
        var documentRequest = await db.DocumentRequests
            .FirstOrDefaultAsync(
                r => r.Id == request.DocumentRequestId
                  && r.CompanyId == request.CompanyId
                  && r.EmployeeId == request.EmployeeId,
                cancellationToken);

        if (documentRequest is null)
            return Result.Failure(Error.NotFound($"Document request '{request.DocumentRequestId}' was not found."));

        if (documentRequest.Status != DocumentRequestStatus.Requested)
            return Result.Failure(Error.Conflict(
                $"Document request cannot be cancelled (status: {documentRequest.Status})."));

        var documentType = await db.DocumentTypes
            .FirstOrDefaultAsync(dt => dt.Id == documentRequest.DocumentTypeId, cancellationToken);

        var now = clock.UtcNowOffset();
        var documentTypeName = documentType?.Name ?? documentRequest.DocumentTypeId.ToString();

        documentRequest.Cancel(now);
        await db.SaveChangesAsync(cancellationToken);

        await taskCanceller.CancelBySourceEntityAsync(
            request.CompanyId,
            documentRequest.Id,
            TaskSource.Document,
            TaskActionType.Upload,
            cancellationToken);

        await auditPublisher.PublishAsync(new DocumentRequestCancelledAuditEvent(
            request.CompanyId,
            documentRequest.Id,
            request.EmployeeId,
            documentTypeName,
            cancelledBy,
            now), cancellationToken);

        return Result.Success();
    }
}
