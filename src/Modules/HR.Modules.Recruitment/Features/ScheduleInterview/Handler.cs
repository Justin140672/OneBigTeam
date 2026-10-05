using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ScheduleInterview;

internal sealed class ScheduleInterviewHandler(
    RecruitmentDbContext db,
    INotificationWriter notificationWriter,
    IClock clock,
    IPositionProfileReader positionProfileReader,
    RecruitmentStageChangeRecorder recorder,
    InterviewTaskEffectsService effectsService)
{
    public async Task<Result<ScheduleInterviewResponse>> HandleAsync(
        ScheduleInterviewRequest request,
        Guid scheduledBy,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, ScheduleInterviewResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    await effectsService.RunOutstandingForApplicationAsync(request.CompanyId, request.ApplicationId, cancellationToken);
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<ScheduleInterviewResponse>(
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
            return Result.Failure<ScheduleInterviewResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        if (application.WithdrawnAt is not null)
            return Result.Failure<ScheduleInterviewResponse>(
                Error.Validation("Cannot schedule an interview for an application that has been withdrawn."));

        // Server-side enforcement (not just UI hiding): an inactive candidate must not be able to
        // pick up new recruitment activity, per the candidate deactivation ticket.
        var candidateIsActive = await db.Candidates
            .AsNoTracking()
            .Where(c => c.Id == application.CandidateId && c.CompanyId == request.CompanyId)
            .Select(c => c.IsActive)
            .SingleOrDefaultAsync(cancellationToken);

        if (!candidateIsActive)
            return Result.Failure<ScheduleInterviewResponse>(
                Error.Validation("Cannot schedule an interview for an inactive candidate."));

        var currentStage = await db.RecruitmentStages
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == application.CurrentStageId && s.CompanyId == request.CompanyId, cancellationToken);

        if (currentStage is { IsTerminal: true })
            return Result.Failure<ScheduleInterviewResponse>(
                Error.Validation($"Cannot schedule an interview for an application already on the terminal stage '{currentStage.Name}'."));

        var now = clock.UtcNowOffset();
        var expectedVersion = application.Version;
        var previousStageId = application.CurrentStageId;

        var interviewStages = await db.RecruitmentStages
            .AsNoTracking()
            .Where(s => s.CompanyId == request.CompanyId && s.IsActive && !s.IsTerminal && s.Purpose == RecruitmentStagePurpose.Interview)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        Guid interviewStageId;
        var movesToInterviewStage = false;

        if (currentStage is { Purpose: RecruitmentStagePurpose.Interview })
        {
            var existingInterviews = await db.Interviews
                .AsNoTracking()
                .Where(i => i.ApplicationId == application.Id && i.CompanyId == request.CompanyId)
                .ToListAsync(cancellationToken);

            var state = InterviewStageWorkflow.Evaluate(currentStage, interviewStages, existingInterviews);

            if (!state.CurrentStageHasPendingInterview && state.LatestOutcome == Domain.InterviewOutcome.Passed && state.HasNextInterviewStage)
                return Result.Failure<ScheduleInterviewResponse>(
                    Error.Validation($"The interview in '{currentStage.Name}' has already been passed. Move the candidate to the next interview stage to schedule the next interview."));

            interviewStageId = currentStage.Id;
        }
        else
        {
            var interviewStage = interviewStages.FirstOrDefault(s =>
                s.Id != application.CurrentStageId
                && (currentStage is null || s.DisplayOrder > currentStage.DisplayOrder));

            if (interviewStage is null)
                return Result.Failure<ScheduleInterviewResponse>(
                    Error.Validation("There is no active interview stage after the current stage to schedule this interview in."));

            movesToInterviewStage = true;
            interviewStageId = interviewStage.Id;

            application.MoveToStage(interviewStage.Id, now);
            recorder.AddHistoryEntry(application, previousStageId, scheduledBy, now);
        }

        application.SetInterviewOutcome(Domain.InterviewOutcome.Pending, now);

        var interview = Interview.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.ApplicationId,
            request.InterviewerEmployeeId,
            request.ScheduledAt,
            request.DurationMinutes,
            request.Location,
            now,
            interviewStageId);

        db.Interviews.Add(interview);

        var candidateName = await db.Candidates
            .Where(c => c.Id == application.CandidateId)
            .Select(c => c.FirstName + " " + c.LastName)
            .SingleOrDefaultAsync(cancellationToken) ?? "the candidate";

        var vacancyFields = await db.Vacancies
            .Where(v => v.Id == application.VacancyId)
            .Select(v => new { v.AdvertTitle, v.PositionProfileId })
            .SingleOrDefaultAsync(cancellationToken);

        var vacancyPositionProfile = vacancyFields is not null
            ? await positionProfileReader.GetSummaryAsync(request.CompanyId, vacancyFields.PositionProfileId, cancellationToken)
            : null;

        var vacancyTitle = vacancyFields?.AdvertTitle ?? vacancyPositionProfile?.Title ?? "this vacancy";

        var effect = InterviewTaskEffect.Create(
            Guid.NewGuid(), request.CompanyId, request.ApplicationId, interview.Id, scheduledBy,
            request.InterviewerEmployeeId, request.ScheduledAt, candidateName, vacancyTitle, now);
        db.InterviewTaskEffects.Add(effect);

        var response = new ScheduleInterviewResponse(
            interview.Id,
            interview.CompanyId,
            interview.ApplicationId,
            interview.InterviewerEmployeeId,
            interview.ScheduledAt,
            interview.DurationMinutes,
            interview.Location,
            interview.Outcome,
            interview.Notes,
            interview.CreatedAt,
            interview.UpdatedAt);

        const string conflictMessage = "This application was changed by someone else. Reload and try again.";

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentWithConcurrencyAsync<IdempotencyRecord, Domain.Application, ScheduleInterviewResponse>(
                db.IdempotencyRecords, application, expectedVersion, scope, key, fingerprint!,
                StatusCodes.Status201Created, response, now, cancellationToken);

            switch (outcome.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    await effectsService.RunOutstandingForApplicationAsync(request.CompanyId, request.ApplicationId, cancellationToken);
                    return Result.Success(outcome.Response!);
                case IdempotencyOutcomeKind.ConcurrencyConflict:
                    return Result.Failure<ScheduleInterviewResponse>(Error.Concurrency(conflictMessage));
            }
        }
        else
        {
            var saveResult = await db.SaveChangesWithConcurrencyAsync(
                application, expectedVersion, conflictMessage, cancellationToken);

            if (!saveResult.IsSuccess)
                return Result.Failure<ScheduleInterviewResponse>(saveResult.Error);
        }

        if (movesToInterviewStage)
            await recorder.PublishStageChangedEventsAsync(application, previousStageId, scheduledBy, now, cancellationToken);

        var interviewStillPending = await effectsService.RunAsync(effect, cancellationToken);

        if (!interviewStillPending)
            return Result.Success(response);

        await notificationWriter.WriteAsync(
            Guid.NewGuid(),
            request.CompanyId,
            request.InterviewerEmployeeId,
            "Interview scheduled",
            $"You have been scheduled to interview {candidateName} for the {vacancyTitle} vacancy on {request.ScheduledAt:d MMM yyyy 'at' HH:mm}.",
            interview.Id,
            NotificationType.InterviewScheduled,
            NotificationPriority.Normal,
            now,
            cancellationToken);

        return Result.Success(response);
    }
}
