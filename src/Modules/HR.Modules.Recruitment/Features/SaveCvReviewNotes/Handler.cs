using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.SaveCvReviewNotes;

/// <summary>
/// Ticket 1 — "Save Notes": records the recruiter's CV review notes against the application without
/// changing the recruitment stage. Notes belong to the Application (this candidate considered for
/// this vacancy). Reuses the existing tenant + vacancy scoping every application handler applies.
/// </summary>
internal sealed class SaveCvReviewNotesHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<SaveCvReviewNotesResponse>> HandleAsync(
        SaveCvReviewNotesRequest request,
        Guid performedBy,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, SaveCvReviewNotesResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<SaveCvReviewNotesResponse>(
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
            return Result.Failure<SaveCvReviewNotesResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        if (application.WithdrawnAt is not null)
            return Result.Failure<SaveCvReviewNotesResponse>(
                Error.Validation("Cannot record a CV review against a withdrawn application."));

        var now = clock.UtcNowOffset();
        var expectedVersion = application.Version;
        application.RecordCvReview(request.CvReviewNotes, performedBy, now);

        var response = new SaveCvReviewNotesResponse(
            application.Id,
            application.VacancyId,
            application.CandidateId,
            application.CurrentStageId,
            application.CvReviewNotes,
            application.CvReviewedAt,
            application.CvReviewedByUserId,
            application.UpdatedAt);

        const string conflictMessage = "This application was changed by someone else. Reload and try again.";

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentWithConcurrencyAsync<IdempotencyRecord, Domain.Application, SaveCvReviewNotesResponse>(
                db.IdempotencyRecords, application, expectedVersion, scope, key, fingerprint!,
                StatusCodes.Status200OK, response, now, cancellationToken);

            switch (outcome.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(outcome.Response!);
                case IdempotencyOutcomeKind.ConcurrencyConflict:
                    return Result.Failure<SaveCvReviewNotesResponse>(Error.Concurrency(conflictMessage));
            }
        }
        else
        {
            var saveResult = await db.SaveChangesWithConcurrencyAsync(
                application, expectedVersion, conflictMessage, cancellationToken);

            if (!saveResult.IsSuccess)
                return Result.Failure<SaveCvReviewNotesResponse>(saveResult.Error);
        }

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

        return Result.Success(response);
    }
}
