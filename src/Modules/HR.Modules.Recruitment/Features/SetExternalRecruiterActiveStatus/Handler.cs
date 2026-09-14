using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.SetExternalRecruiterActiveStatus;

internal sealed class SetExternalRecruiterActiveStatusHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<SetExternalRecruiterActiveStatusResponse>> HandleAsync(
        SetExternalRecruiterActiveStatusRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, SetExternalRecruiterActiveStatusResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<SetExternalRecruiterActiveStatusResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var recruiter = await db.ExternalRecruiters
            .SingleOrDefaultAsync(
                r => r.Id == request.ExternalRecruiterId && r.CompanyId == request.CompanyId,
                cancellationToken);

        if (recruiter is null)
            return Result.Failure<SetExternalRecruiterActiveStatusResponse>(
                Error.NotFound($"External recruiter '{request.ExternalRecruiterId}' was not found."));

        var previousIsActive = recruiter.IsActive;
        var now = clock.UtcNowOffset();

        // Never deletes the row — just flips the flag, per SetActiveStatus's remarks.
        recruiter.SetActiveStatus(request.IsActive, now);

        var response = new SetExternalRecruiterActiveStatusResponse(
            recruiter.Id,
            recruiter.CompanyId,
            recruiter.AgencyName,
            recruiter.IsActive,
            recruiter.UpdatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, SetExternalRecruiterActiveStatusResponse>(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await auditPublisher.PublishAsync(
            new ExternalRecruiterActiveStatusChangedAuditEvent(
                recruiter.CompanyId, recruiter.Id, recruiter.AgencyName, previousIsActive, recruiter.IsActive, now),
            cancellationToken);

        return Result.Success(response);
    }
}
