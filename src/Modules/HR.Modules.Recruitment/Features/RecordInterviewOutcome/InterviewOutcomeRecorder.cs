using HR.Modules.Tasks.Contracts;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

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
    IAuditEventPublisher auditPublisher)
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

        // Ticket #99 judgement call: recording an interview outcome is metadata only (mirrored onto
        // Application.InterviewOutcome for cheap list/kanban display) — it never itself moves
        // CurrentStageId, since "Interview" is just one configurable stage among however many the
        // company defines. No stage-history entry or ApplicantStageChangedIntegrationEvent is
        // published here, since the stage does not change; advancing the pipeline (e.g. to an
        // "Offer" stage) remains a separate, explicit action via MoveApplicationStage/OfferCandidate.
        interview.RecordOutcome(request.Outcome, request.Notes, now);
        application.SetInterviewOutcome(request.Outcome, now);
        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(
            new InterviewOutcomeRecordedAuditEvent(
                interview.CompanyId,
                interview.Id,
                application.Id,
                application.VacancyId,
                application.CandidateId,
                interview.Outcome,
                interview.Notes,
                recordedBy,
                now),
            cancellationToken);

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
