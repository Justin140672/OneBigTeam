using HR.Modules.Tasks.Contracts;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
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
        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated request before creating a second
        // document request for the same logical request.
                var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);
        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(new RequestAdditionalEmployeeDocumentRequest
            {
                CompanyId = request.CompanyId,
                EmployeeId = request.EmployeeId,
                DocumentTypeId = request.DocumentTypeId,
                DueDate = request.DueDate,
                IsMandatory = request.IsMandatory,
                Notes = request.Notes,
            })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, RequestAdditionalEmployeeDocumentResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<RequestAdditionalEmployeeDocumentResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

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
            request.IsMandatory,
            request.Notes,
            requestedByEmployeeId: requestedBy,
            now);

        db.DocumentRequests.Add(documentRequest);

        var response = new RequestAdditionalEmployeeDocumentResponse(
            documentRequest.Id,
            documentRequest.CompanyId,
            documentRequest.EmployeeId,
            documentRequest.DocumentTypeId,
            documentType.Name,
            documentRequest.DueDate,
            documentRequest.IsMandatory,
            documentRequest.Notes,
            documentRequest.Status.ToString(),
            documentRequest.CreatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync(db.IdempotencyRecords,
            scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

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

        return Result.Success(response);
    }
}
