using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ReactivateCandidate;

internal sealed class ReactivateCandidateHandler(RecruitmentDbContext db, IClock clock, IAuditEventPublisher auditPublisher)
{
    public async Task<Result<ReactivateCandidateResponse>> HandleAsync(
        ReactivateCandidateRequest request,
        Guid performedBy,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, ReactivateCandidateResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<ReactivateCandidateResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var candidate = await db.Candidates
            .SingleOrDefaultAsync(
                c => c.Id == request.CandidateId && c.CompanyId == request.CompanyId,
                cancellationToken);

        if (candidate is null)
            return Result.Failure<ReactivateCandidateResponse>(
                Error.NotFound($"Candidate '{request.CandidateId}' was not found."));

        if (candidate.IsActive)
            return Result.Failure<ReactivateCandidateResponse>(
                Error.Conflict("This candidate is already active."));

        var now = clock.UtcNowOffset();

        candidate.Reactivate(performedBy, now);

        var response = new ReactivateCandidateResponse(
            candidate.Id,
            candidate.CompanyId,
            candidate.IsActive,
            candidate.ReactivatedAt,
            candidate.ReactivatedByUserId,
            candidate.UpdatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, ReactivateCandidateResponse>(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await auditPublisher.PublishAsync(
            new CandidateReactivatedAuditEvent(
                candidate.CompanyId,
                candidate.Id,
                $"{candidate.FirstName} {candidate.LastName}",
                performedBy,
                now),
            cancellationToken);

        return Result.Success(response);
    }
}
