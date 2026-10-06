using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.WithdrawApplication;

internal sealed class WithdrawApplicationHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    InternalOfferTaskEffectsService? internalOfferEffects = null)
{
    public async Task<Result<WithdrawApplicationResponse>> HandleAsync(
        WithdrawApplicationRequest request,
        Guid performedBy,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, WithdrawApplicationResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<WithdrawApplicationResponse>(
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
            return Result.Failure<WithdrawApplicationResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        if (application.WithdrawnAt is not null)
            return Result.Failure<WithdrawApplicationResponse>(
                Error.Validation("This application has already been withdrawn."));

        // Internal recruitment Ticket 7: see RejectCandidate — no withdrawal mid-appointment.
        if (application.HasInternalAppointmentInProgress)
            return Result.Failure<WithdrawApplicationResponse>(
                Error.Conflict(Domain.Application.InternalAppointmentInProgressMessage));

        var currentStage = await db.RecruitmentStages
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == application.CurrentStageId && s.CompanyId == request.CompanyId, cancellationToken);

        if (currentStage is { IsTerminal: true })
            return Result.Failure<WithdrawApplicationResponse>(
                Error.Validation($"Cannot withdraw an application already on the terminal stage '{currentStage.Name}'."));

        var now = clock.UtcNowOffset();
        var expectedVersion = application.Version;

        application.Withdraw(now);

        var pendingInterviews = await db.Interviews
            .Where(i => i.ApplicationId == application.Id && i.CompanyId == request.CompanyId
                && i.Outcome == HR.Modules.Recruitment.Domain.InterviewOutcome.Pending)
            .ToListAsync(cancellationToken);

        foreach (var interview in pendingInterviews)
            interview.Cancel(now);

        var response = new WithdrawApplicationResponse(
            application.Id,
            application.VacancyId,
            application.CandidateId,
            application.CurrentStageId,
            application.InterviewOutcome,
            application.Notes,
            application.WithdrawnAt,
            application.AppliedAt,
            application.CreatedAt,
            application.UpdatedAt);

        const string conflictMessage = "This application was changed by someone else. Reload and try again.";

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentWithConcurrencyAsync<IdempotencyRecord, HR.Modules.Recruitment.Domain.Application, WithdrawApplicationResponse>(
                db.IdempotencyRecords, application, expectedVersion, scope, key, fingerprint!,
                StatusCodes.Status200OK, response, now, cancellationToken);

            switch (outcome.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(outcome.Response!);
                case IdempotencyOutcomeKind.ConcurrencyConflict:
                    return Result.Failure<WithdrawApplicationResponse>(Error.Concurrency(conflictMessage));
            }
        }
        else
        {
            var saveResult = await db.SaveChangesWithConcurrencyAsync(
                application, expectedVersion, conflictMessage, cancellationToken);

            if (!saveResult.IsSuccess)
                return Result.Failure<WithdrawApplicationResponse>(saveResult.Error);
        }

        await auditPublisher.PublishAsync(
            new ApplicationWithdrawnAuditEvent(
                application.CompanyId,
                application.Id,
                application.VacancyId,
                application.CandidateId,
                application.CurrentStageId,
                performedBy,
                now),
            cancellationToken);

        if (internalOfferEffects is not null)
            await internalOfferEffects.RunOutstandingForApplicationAsync(request.CompanyId, request.ApplicationId, cancellationToken);

        return Result.Success(response);
    }
}
