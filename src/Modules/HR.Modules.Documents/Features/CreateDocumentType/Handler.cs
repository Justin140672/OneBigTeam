using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.CreateDocumentType;

internal sealed class CreateDocumentTypeHandler(DocumentsDbContext db, IClock clock)
{
    public async Task<Result<CreateDocumentTypeResponse>> HandleAsync(
        CreateDocumentTypeRequest request,
        CancellationToken cancellationToken)
    {
        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated request before creating a second
        // document type for the same logical request.
                var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);
        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, CreateDocumentTypeResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CreateDocumentTypeResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var nameExists = await db.DocumentTypes
            .AnyAsync(
                dt => dt.CompanyId == request.CompanyId &&
                      dt.Name.ToLower() == request.Name.Trim().ToLower() &&
                      dt.IsActive,
                cancellationToken);

        if (nameExists)
        {
            return Result.Failure<CreateDocumentTypeResponse>(
                Error.Conflict($"An active document type named '{request.Name.Trim()}' already exists in this company."));
        }

        var now = clock.UtcNowOffset();

        var documentType = DocumentType.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.Name,
            request.Description,
            now,
            request.AllowEmployeeUpload);

        db.DocumentTypes.Add(documentType);

        var response = new CreateDocumentTypeResponse(
            documentType.Id,
            documentType.CompanyId,
            documentType.Name,
            documentType.Description,
            documentType.IsActive,
            documentType.AllowEmployeeUpload,
            documentType.CreatedAt);

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

        return Result.Success(response);
    }
}
