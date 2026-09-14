using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.RejectCandidate;

internal sealed class RejectCandidateHandler(RecruitmentDbContext db, IClock clock, RecruitmentStageChangeRecorder recorder)
{
    public async Task<Result<RejectCandidateResponse>> HandleAsync(
        RejectCandidateRequest request,
        Guid performedBy,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, RejectCandidateResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<RejectCandidateResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var application = await db.Applications
            .SingleOrDefaultAsync(
                a => a.Id == request.ApplicationId &&
                     a.CompanyId == request.CompanyId &&
                     a.VacancyId == request.VacancyId,
                cancellationToken);

        if (application is null)
            return Result.Failure<RejectCandidateResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        if (application.WithdrawnAt is not null)
            return Result.Failure<RejectCandidateResponse>(
                Error.Validation("Cannot reject an application that has been withdrawn."));

        var currentStage = await db.RecruitmentStages
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == application.CurrentStageId && s.CompanyId == request.CompanyId, cancellationToken);

        if (currentStage is null)
            return Result.Failure<RejectCandidateResponse>(
                Error.NotFound($"Recruitment stage '{application.CurrentStageId}' was not found."));

        if (currentStage.IsTerminal)
            return Result.Failure<RejectCandidateResponse>(
                Error.Validation($"Cannot reject an application already on the terminal stage '{currentStage.Name}'."));

        var rejectedStage = await db.RecruitmentStages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                s => s.CompanyId == request.CompanyId && s.IsActive && s.TerminalOutcome == RecruitmentStageTerminalOutcome.Rejected,
                cancellationToken);

        if (rejectedStage is null)
            return Result.Failure<RejectCandidateResponse>(
                Error.Validation("This company has no active 'Rejected' terminal recruitment stage configured."));

        var now = clock.UtcNowOffset();
        var previousStageId = application.CurrentStageId;

        application.RecordRejection(rejectedStage.Id, request.RejectionReason, now);
        recorder.AddHistoryEntry(application, previousStageId, performedBy, now, request.RejectionReason);

        var response = new RejectCandidateResponse(
            application.Id,
            application.VacancyId,
            application.CandidateId,
            application.CurrentStageId,
            application.InterviewOutcome,
            application.Notes,
            application.RejectionReason,
            application.AppliedAt,
            application.CreatedAt,
            application.UpdatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, RejectCandidateResponse>(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await recorder.PublishStageChangedEventsAsync(application, previousStageId, performedBy, now, cancellationToken);

        return Result.Success(response);
    }
}
