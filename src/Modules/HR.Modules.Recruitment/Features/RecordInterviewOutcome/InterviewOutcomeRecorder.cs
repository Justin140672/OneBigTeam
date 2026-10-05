using HR.Modules.Tasks.Contracts;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Features.RecordInterviewOutcome;

/// <summary>
/// Contains the core interview-outcome-recording logic (validation, persistence, audit event).
/// Deliberately excludes task-completion so it can be shared by both:
///  - <see cref="RecordInterviewOutcomeHandler"/>, used by the direct "record outcome" API endpoint,
///    where completing the associated feedback task is a required side-effect, and
///  - <c>InterviewFeedbackService</c>, used by the generic task-completion path, where the
///    originating task has already been marked complete by the caller before this runs.
/// Keeping this class free of <see cref="ITaskCompleter"/> avoids a DI constructor cycle between
/// the Recruitment and Tasks modules (Tasks' task-completion dispatch can invoke recruitment
/// feedback recording without that recording logic looping back into task completion).
/// </summary>
internal sealed class InterviewOutcomeRecorder(
    RecruitmentDbContext db,
    IClock clock,
    InterviewOutcomeAuditDelivery auditDelivery,
    ILogger<InterviewOutcomeRecorder> logger)
{
    public async Task<Result<RecordInterviewOutcomeResponse>> RecordAsync(
        RecordInterviewOutcomeRequest request,
        Guid recordedBy,
        CancellationToken cancellationToken)
    {
        var application = await db.Applications
            .SingleOrDefaultAsync(
                a => a.Id == request.ApplicationId &&
                     a.CompanyId == request.CompanyId &&
                     a.VacancyId == request.VacancyId,
                cancellationToken);

        if (application is null)
            return Result.Failure<RecordInterviewOutcomeResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        var interview = await db.Interviews
            .SingleOrDefaultAsync(
                i => i.Id == request.InterviewId &&
                     i.ApplicationId == request.ApplicationId &&
                     i.CompanyId == request.CompanyId,
                cancellationToken);

        if (interview is null)
            return Result.Failure<RecordInterviewOutcomeResponse>(
                Error.NotFound($"Interview '{request.InterviewId}' was not found."));

        if (interview.Outcome != Domain.InterviewOutcome.Pending)
            return Result.Failure<RecordInterviewOutcomeResponse>(
                Error.Validation($"Cannot record an outcome for an interview with outcome '{interview.Outcome}'."));

        var now = clock.UtcNowOffset();

        if (interview.StageId is null)
        {
            var currentStageId = await db.RecruitmentStages
                .AsNoTracking()
                .Where(s => s.Id == application.CurrentStageId && s.Purpose == Domain.RecruitmentStagePurpose.Interview)
                .Select(s => (Guid?)s.Id)
                .SingleOrDefaultAsync(cancellationToken);

            if (currentStageId is { } legacyStageId)
                interview.AssignStage(legacyStageId);
        }

        interview.RecordOutcome(request.Outcome, request.Notes, now);

        var otherPendingExists = await db.Interviews
            .AnyAsync(
                i => i.ApplicationId == application.Id &&
                     i.CompanyId == request.CompanyId &&
                     i.Id != interview.Id &&
                     i.Outcome == Domain.InterviewOutcome.Pending,
                cancellationToken);

        application.SetInterviewOutcome(
            otherPendingExists ? Domain.InterviewOutcome.Pending : request.Outcome, now);
        var reconciliation = Domain.InterviewOutcomeTaskReconciliation.Create(
            Guid.NewGuid(), request.CompanyId, application.Id, interview.Id, recordedBy, now);
        db.InterviewOutcomeTaskReconciliations.Add(reconciliation);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await auditDelivery.DeliverAsync(reconciliation, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Interview outcome audit delivery is unconfirmed and remains outstanding; it will be retried by reconciliation. ReconciliationId={ReconciliationId} CompanyId={CompanyId} InterviewId={InterviewId} ApplicationId={ApplicationId}",
                reconciliation.Id, reconciliation.CompanyId, reconciliation.InterviewId, reconciliation.ApplicationId);
        }

        return Result.Success(new RecordInterviewOutcomeResponse(
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
            interview.UpdatedAt));
    }
}
