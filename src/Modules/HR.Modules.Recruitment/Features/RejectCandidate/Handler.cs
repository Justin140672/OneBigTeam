using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.RejectCandidate;

internal sealed class RejectCandidateHandler(
    RecruitmentDbContext db,
    IClock clock,
    RecruitmentStageChangeRecorder recorder,
    InterviewTaskCleanupService cleanupService)
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
                    await cleanupService.RunOutstandingForApplicationAsync(request.CompanyId, request.ApplicationId, cancellationToken);
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

        // Internal recruitment Ticket 7: the employee change may already be recorded; recovery will
        // move this application to Hired, so it must not be rejected in the meantime.
        if (application.HasInternalAppointmentInProgress)
            return Result.Failure<RejectCandidateResponse>(
                Error.Conflict(Domain.Application.InternalAppointmentInProgressMessage));

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
        var expectedVersion = application.Version;

        application.RecordRejection(rejectedStage.Id, request.RejectionReason, now);

        var pendingInterviews = await db.Interviews
            .Where(i => i.ApplicationId == application.Id && i.CompanyId == request.CompanyId
                && i.Outcome == Domain.InterviewOutcome.Pending)
            .ToListAsync(cancellationToken);

        foreach (var interview in pendingInterviews)
            interview.Cancel(now);

        InterviewTaskCleanup? cleanup = null;
        if (pendingInterviews.Count > 0)
        {
            cleanup = InterviewTaskCleanup.Create(
                Guid.NewGuid(), request.CompanyId, application.Id, pendingInterviews.Select(i => i.Id).ToList(), now);
            db.InterviewTaskCleanups.Add(cleanup);
        }

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

        // Ticket 14 (P2): SaveIdempotentWithConcurrencyAsync pins/advances application's version and
        // translates a stale-save DbUpdateConcurrencyException the same way the non-idempotent
        // branch below does — an Idempotency-Key must not bypass optimistic-concurrency protection.
        const string conflictMessage = "This application was changed by someone else. Reload and try again.";

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentWithConcurrencyAsync<IdempotencyRecord, Application, RejectCandidateResponse>(
                db.IdempotencyRecords, application, expectedVersion, scope, key, fingerprint!,
                StatusCodes.Status200OK, response, now, cancellationToken);

            switch (outcome.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    await cleanupService.RunOutstandingForApplicationAsync(request.CompanyId, request.ApplicationId, cancellationToken);
                    return Result.Success(outcome.Response!);
                case IdempotencyOutcomeKind.ConcurrencyConflict:
                    return Result.Failure<RejectCandidateResponse>(Error.Concurrency(conflictMessage));
            }
        }
        else
        {
            // Ticket 6 (P1): see MoveApplicationStageHandler's matching guard.
            var saveResult = await db.SaveChangesWithConcurrencyAsync(
                application, expectedVersion, conflictMessage, cancellationToken);

            if (!saveResult.IsSuccess)
                return Result.Failure<RejectCandidateResponse>(saveResult.Error);
        }

        if (cleanup is not null)
            await cleanupService.RunAsync(cleanup, cancellationToken);

        await recorder.PublishStageChangedEventsAsync(application, previousStageId, performedBy, now, cancellationToken);

        return Result.Success(response);
    }
}
