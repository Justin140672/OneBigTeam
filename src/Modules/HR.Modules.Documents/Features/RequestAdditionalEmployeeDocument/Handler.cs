using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.RequestAdditionalEmployeeDocument;

internal sealed class RequestAdditionalEmployeeDocumentHandler(
    DocumentsDbContext db,
    ITaskCreator taskCreator,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<RequestAdditionalEmployeeDocumentResponse>> HandleAsync(
        RequestAdditionalEmployeeDocumentRequest request,
        Guid requestedBy,
        CancellationToken cancellationToken)
    {
        var documentType = await db.DocumentTypes
            .FirstOrDefaultAsync(
                dt => dt.Id == request.DocumentTypeId
                   && dt.CompanyId == request.CompanyId
                   && dt.IsActive,
                cancellationToken);

        if (documentType is null)
            return Result.Failure<RequestAdditionalEmployeeDocumentResponse>(
                Error.NotFound($"Document type '{request.DocumentTypeId}' was not found."));

        var alreadyExists = await db.DocumentRequests
            .AnyAsync(
                r => r.EmployeeId      == request.EmployeeId
                  && r.DocumentTypeId  == request.DocumentTypeId,
                cancellationToken);

        if (alreadyExists)
            return Result.Failure<RequestAdditionalEmployeeDocumentResponse>(
                Error.Conflict($"A document request for '{documentType.Name}' already exists for this employee."));

        var now = clock.UtcNowOffset();

        var documentRequest = DocumentRequest.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            request.DocumentTypeId,
            positionProfileRequiredDocumentId: null,
            request.DueDate,
            requestedByEmployeeId: requestedBy,
            now);

        db.DocumentRequests.Add(documentRequest);
        await db.SaveChangesAsync(cancellationToken);

        await taskCreator.CreateAsync(
            request.CompanyId,
            createdBy:          requestedBy,
            title:              $"Upload {documentType.Name}",
            description:        $"Please upload a copy of your {documentType.Name}.",
            priority:           TaskPriority.Medium,
            source:             TaskSource.Document,
            actionType:         TaskActionType.Upload,
            dueDate:            request.DueDate,
            assignedEmployeeId: request.EmployeeId,
            assignedUserId:     null,
            sourceEntityId:     documentRequest.Id,
            cancellationToken);

        await auditPublisher.PublishAsync(new DocumentRequestedAuditEvent(
            request.CompanyId,
            documentRequest.Id,
            request.EmployeeId,
            documentType.Name,
            request.DueDate,
            requestedBy,
            now), cancellationToken);

        return Result.Success(new RequestAdditionalEmployeeDocumentResponse(
            documentRequest.Id,
            documentRequest.CompanyId,
            documentRequest.EmployeeId,
            documentRequest.DocumentTypeId,
            documentType.Name,
            documentRequest.DueDate,
            documentRequest.Status.ToString(),
            documentRequest.CreatedAt));
    }
}
