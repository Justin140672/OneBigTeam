using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.MoveApplicationStage;

/// <summary>
/// Generic Kanban drag-and-drop move: unlike the named transition endpoints (Offer/Hire/Reject/etc.),
/// the caller here only knows the target RecruitmentStage id. Ticket #99: since stages are now fully
/// data-driven (no compiled ApplicationStatusTransitions graph any more), validity here is simply
/// "the target stage exists, belongs to this company, and is active" plus "the application isn't
/// withdrawn or already on a terminal stage" — a company may freely reorder/insert stages, so no
/// stricter linear-order transition check is enforced. Records stage history (#66) and publishes the
/// integration + audit events (#65/#67) exactly once, the same way the named-transition handlers do
/// via RecruitmentStageChangeRecorder.
/// </summary>
internal sealed class MoveApplicationStageHandler(
    RecruitmentDbContext db,
    IClock clock,
    RecruitmentStageChangeRecorder recorder)
{
    public async Task<Result<MoveApplicationStageResponse>> HandleAsync(
        MoveApplicationStageRequest request,
        Guid performedBy,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, MoveApplicationStageResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<MoveApplicationStageResponse>(
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
            return Result.Failure<MoveApplicationStageResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        if (application.WithdrawnAt is not null)
            return Result.Failure<MoveApplicationStageResponse>(
                Error.Validation("Cannot move a withdrawn application to a different stage."));

        var currentStage = await db.RecruitmentStages
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == application.CurrentStageId && s.CompanyId == request.CompanyId, cancellationToken);

        if (currentStage is { IsTerminal: true })
            return Result.Failure<MoveApplicationStageResponse>(
                Error.Validation($"Cannot move an application off the terminal stage '{currentStage.Name}'."));

        var newStage = await db.RecruitmentStages
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == request.NewStageId && s.CompanyId == request.CompanyId, cancellationToken);

        if (newStage is null)
            return Result.Failure<MoveApplicationStageResponse>(
                Error.NotFound($"Recruitment stage '{request.NewStageId}' was not found."));

        if (!newStage.IsActive)
            return Result.Failure<MoveApplicationStageResponse>(
                Error.Validation($"Cannot move an application to the inactive stage '{newStage.Name}'."));

        // Ticket 5 (P1): a terminal stage (Hired/Rejected/Withdrawn, or any other company-configured
        // terminal outcome) always has required side effects owned by a dedicated workflow endpoint
        // — most importantly HireCandidate, which provisions the Employee record. This generic
        // Kanban move has no such side effects, so allowing it to target a terminal stage directly
        // let an application reach "Hired" with no Employee ever created (see HireCandidateHandler,
        // which performs that provisioning and is the ONLY sanctioned way onto a Hired stage).
        // Reject/Withdraw already have their own dedicated endpoints too; route every terminal
        // transition through them instead of this generic move.
        if (newStage.IsTerminal)
            return Result.Failure<MoveApplicationStageResponse>(
                Error.Validation(
                    $"Cannot move an application to the terminal stage '{newStage.Name}' via a generic stage move — " +
                    "use the dedicated workflow action for this outcome (e.g. Hire, Reject, Withdraw)."));

        var previousStageId = application.CurrentStageId;
        var expectedVersion = application.Version;
        var now = clock.UtcNowOffset();

        application.MoveToStage(newStage.Id, now);

        recorder.AddHistoryEntry(application, previousStageId, performedBy, now, request.Notes);

        var response = new MoveApplicationStageResponse(
            application.Id,
            application.VacancyId,
            application.CandidateId,
            application.CurrentStageId,
            application.InterviewOutcome,
            application.Notes,
            application.AppliedAt,
            application.CreatedAt,
            application.UpdatedAt);

        // Ticket 14 (P2): SaveIdempotentWithConcurrencyAsync pins/advances application's version and
        // translates a stale-save DbUpdateConcurrencyException the same way the non-idempotent
        // branch below does — an Idempotency-Key must not bypass optimistic-concurrency protection.
        const string conflictMessage = "This application was changed by someone else. Reload and try again.";

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentWithConcurrencyAsync<IdempotencyRecord, Application, MoveApplicationStageResponse>(
                db.IdempotencyRecords, application, expectedVersion, scope, key, fingerprint!,
                StatusCodes.Status200OK, response, now, cancellationToken);

            switch (outcome.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(outcome.Response!);
                case IdempotencyOutcomeKind.ConcurrencyConflict:
                    return Result.Failure<MoveApplicationStageResponse>(Error.Concurrency(conflictMessage));
            }
        }
        else
        {
            // Ticket 6 (P1): pin the version read at the top of this handler so a concurrent writer
            // (another generic move, or a named transition like Hire/Reject) that already saved
            // since we read is detected instead of silently overwritten.
            var saveResult = await db.SaveChangesWithConcurrencyAsync(
                application, expectedVersion, conflictMessage, cancellationToken);

            if (!saveResult.IsSuccess)
                return Result.Failure<MoveApplicationStageResponse>(saveResult.Error);
        }

        await recorder.PublishStageChangedEventsAsync(application, previousStageId, performedBy, now, cancellationToken);

        return Result.Success(response);
    }
}
