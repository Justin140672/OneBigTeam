using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.CreateCompanyDocumentCategory;

internal sealed class CreateCompanyDocumentCategoryHandler(DocumentsDbContext db, IClock clock)
{
    public async Task<Result<CreateCompanyDocumentCategoryResponse>> HandleAsync(
        CreateCompanyDocumentCategoryRequest request,
        CancellationToken cancellationToken)
    {
        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated request before creating a second
        // category for the same logical request.
                var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);
        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, CreateCompanyDocumentCategoryResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CreateCompanyDocumentCategoryResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var name = request.Name.Trim();

        var nameExists = await db.CompanyDocumentCategories
            .AnyAsync(
                c => c.CompanyId == request.CompanyId &&
                     c.Name == name &&
                     c.IsActive,
                cancellationToken);

        if (nameExists)
        {
            return Result.Failure<CreateCompanyDocumentCategoryResponse>(
                Error.Conflict($"An active document category named '{name}' already exists in this company."));
        }

        var now = clock.UtcNowOffset();

        var category = CompanyDocumentCategory.Create(Guid.NewGuid(), request.CompanyId, name, now);

        db.CompanyDocumentCategories.Add(category);

        var response = new CreateCompanyDocumentCategoryResponse(
            category.Id,
            category.CompanyId,
            category.Name,
            category.IsActive,
            category.CreatedAt);

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
