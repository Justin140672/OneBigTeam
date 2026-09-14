using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.CreateRecruitmentStage;

internal sealed class CreateRecruitmentStageHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<CreateRecruitmentStageResponse>> HandleAsync(
        CreateRecruitmentStageRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, CreateRecruitmentStageResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CreateRecruitmentStageResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var trimmedName = request.Name.Trim();

        // Duplicate stage names within a company are rejected (ticket #97).
        var duplicateName = await db.RecruitmentStages
            .AnyAsync(s => s.CompanyId == request.CompanyId && s.Name == trimmedName, cancellationToken);

        if (duplicateName)
            return Result.Failure<CreateRecruitmentStageResponse>(
                Error.Validation($"A recruitment stage named '{trimmedName}' already exists."));

        // Unique DisplayOrder within a company.
        var duplicateDisplayOrder = await db.RecruitmentStages
            .AnyAsync(s => s.CompanyId == request.CompanyId && s.DisplayOrder == request.DisplayOrder, cancellationToken);

        if (duplicateDisplayOrder)
            return Result.Failure<CreateRecruitmentStageResponse>(
                Error.Validation($"A recruitment stage with display order '{request.DisplayOrder}' already exists."));

        // Exactly one active stage per TerminalOutcome value (Hired/Rejected) is allowed at a time.
        if (request.IsTerminal && request.TerminalOutcome != RecruitmentStageTerminalOutcome.None)
        {
            var duplicateTerminalOutcome = await db.RecruitmentStages
                .AnyAsync(
                    s => s.CompanyId == request.CompanyId && s.IsActive && s.TerminalOutcome == request.TerminalOutcome,
                    cancellationToken);

            if (duplicateTerminalOutcome)
                return Result.Failure<CreateRecruitmentStageResponse>(
                    Error.Validation($"An active recruitment stage with terminal outcome '{request.TerminalOutcome}' already exists."));
        }

        var now = clock.UtcNowOffset();

        var stage = RecruitmentStage.Create(
            Guid.NewGuid(),
            request.CompanyId,
            trimmedName,
            request.DisplayOrder,
            request.IsTerminal,
            request.TerminalOutcome,
            now,
            request.Purpose);

        db.RecruitmentStages.Add(stage);

        var response = new CreateRecruitmentStageResponse(
            stage.Id,
            stage.CompanyId,
            stage.Name,
            stage.DisplayOrder,
            stage.IsActive,
            stage.IsTerminal,
            stage.TerminalOutcome,
            stage.Purpose,
            stage.CreatedAt,
            stage.UpdatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, CreateRecruitmentStageResponse>(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await auditPublisher.PublishAsync(
            new RecruitmentStageCreatedAuditEvent(
                stage.CompanyId, stage.Id, stage.Name, stage.DisplayOrder, stage.IsTerminal, stage.TerminalOutcome, now),
            cancellationToken);

        return Result.Success(response);
    }
}
