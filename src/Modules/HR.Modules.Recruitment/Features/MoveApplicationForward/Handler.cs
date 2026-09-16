using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.MoveApplicationForward;

/// <summary>
/// Ticket 1 — "Move Forward": saves the recruiter's CV review notes (if supplied) and advances the
/// application to the next active recruitment stage. The "next" stage is resolved purely from the
/// company's own configurable stage ordering — the first active, non-terminal
/// <see cref="Domain.RecruitmentStage"/> whose <c>DisplayOrder</c> is greater than the current
/// stage's. Nothing is hard-coded (no assumption that "Interview" follows "CV Review"). The stage
/// change goes through the shared <see cref="RecruitmentStageChangeRecorder"/> so history, audit,
/// integration events, reporting and the Kanban board all stay consistent with every other move.
/// </summary>
internal sealed class MoveApplicationForwardHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    RecruitmentStageChangeRecorder recorder)
{
    public async Task<Result<MoveApplicationForwardResponse>> HandleAsync(
        MoveApplicationForwardRequest request,
        Guid performedBy,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, MoveApplicationForwardResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<MoveApplicationForwardResponse>(
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
            return Result.Failure<MoveApplicationForwardResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        if (application.WithdrawnAt is not null)
            return Result.Failure<MoveApplicationForwardResponse>(
                Error.Validation("Cannot move a withdrawn application to a different stage."));

        var currentStage = await db.RecruitmentStages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                s => s.Id == application.CurrentStageId && s.CompanyId == request.CompanyId,
                cancellationToken);

        if (currentStage is null)
            return Result.Failure<MoveApplicationForwardResponse>(
                Error.NotFound($"Recruitment stage '{application.CurrentStageId}' was not found."));

        if (currentStage.IsTerminal)
            return Result.Failure<MoveApplicationForwardResponse>(
                Error.Validation($"Cannot move an application forward from the terminal stage '{currentStage.Name}'."));

        var nextStage = await db.RecruitmentStages
            .AsNoTracking()
            .Where(s => s.CompanyId == request.CompanyId &&
                        s.IsActive &&
                        !s.IsTerminal &&
                        s.DisplayOrder > currentStage.DisplayOrder)
            .OrderBy(s => s.DisplayOrder)
            .FirstOrDefaultAsync(cancellationToken);

        if (nextStage is null)
            return Result.Failure<MoveApplicationForwardResponse>(
                Error.Validation($"There is no active recruitment stage after '{currentStage.Name}' to move this application forward to."));

        var now = clock.UtcNowOffset();
        var previousStageId = application.CurrentStageId;
        var expectedVersion = application.Version;

        var notesProvided = request.CvReviewNotes is not null;
        if (notesProvided)
            application.RecordCvReview(request.CvReviewNotes, performedBy, now);

        application.MoveToStage(nextStage.Id, now);
        recorder.AddHistoryEntry(application, previousStageId, performedBy, now, request.CvReviewNotes);

        var response = new MoveApplicationForwardResponse(
            application.Id,
            application.VacancyId,
            application.CandidateId,
            previousStageId,
            application.CurrentStageId,
            nextStage.Name,
            application.CvReviewNotes,
            application.UpdatedAt);

        // Ticket 14 (P2): SaveIdempotentWithConcurrencyAsync pins/advances application's version and
        // translates a stale-save DbUpdateConcurrencyException the same way the non-idempotent
        // branch below does — an Idempotency-Key must not bypass optimistic-concurrency protection.
        const string conflictMessage = "This application was changed by someone else. Reload and try again.";

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentWithConcurrencyAsync<IdempotencyRecord, Application, MoveApplicationForwardResponse>(
                db.IdempotencyRecords, application, expectedVersion, scope, key, fingerprint!,
                StatusCodes.Status200OK, response, now, cancellationToken);

            switch (outcome.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(outcome.Response!);
                case IdempotencyOutcomeKind.ConcurrencyConflict:
                    return Result.Failure<MoveApplicationForwardResponse>(Error.Concurrency(conflictMessage));
            }
        }
        else
        {
            // Ticket 6 (P1): see MoveApplicationStageHandler's matching guard.
            var saveResult = await db.SaveChangesWithConcurrencyAsync(
                application, expectedVersion, conflictMessage, cancellationToken);

            if (!saveResult.IsSuccess)
                return Result.Failure<MoveApplicationForwardResponse>(saveResult.Error);
        }

        if (notesProvided)
            await auditPublisher.PublishAsync(
                new ApplicationCvReviewNotesSavedAuditEvent(
                    application.CompanyId,
                    application.Id,
                    application.VacancyId,
                    application.CandidateId,
                    NotesCleared: application.CvReviewNotes is null,
                    performedBy,
                    now),
                cancellationToken);

        await recorder.PublishStageChangedEventsAsync(application, previousStageId, performedBy, now, cancellationToken);

        return Result.Success(response);
    }
}
