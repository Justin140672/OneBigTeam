using HR.Modules.Tasks.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ScheduleInterview;

internal sealed class ScheduleInterviewHandler(
    RecruitmentDbContext db,
    ITaskCreator taskCreator,
    INotificationWriter notificationWriter,
    IClock clock,
    IPositionProfileReader positionProfileReader)
{
    public async Task<Result<ScheduleInterviewResponse>> HandleAsync(
        ScheduleInterviewRequest request,
        Guid scheduledBy,
        CancellationToken cancellationToken)
    {
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

        // Ticket #99 judgement call: interview sub-states (Screening/InterviewScheduled/Interviewed)
        // no longer exist as separate pipeline stages — "Interview" is just one configurable stage
        // among however many a company defines, and a company may have zero, one, or several
        // interview-shaped stages. Scheduling an interview is therefore pure metadata (an Interview
        // row plus Application.InterviewOutcome defaulting to Pending) and never itself moves
        // CurrentStageId — moving stage remains an explicit, separate action via
        // MoveApplicationStage/OfferCandidate/etc. No stage-history entry/integration/audit event is
        // recorded here, since the stage does not change.
        if (application.InterviewOutcome is null)
            application.SetInterviewOutcome(Domain.InterviewOutcome.Pending, now);

        var interview = Interview.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.ApplicationId,
            request.InterviewerEmployeeId,
            request.ScheduledAt,
            request.DurationMinutes,
            request.Location,
            now);

        db.Interviews.Add(interview);
        await db.SaveChangesAsync(cancellationToken);

        var candidateName = await db.Candidates
            .Where(c => c.Id == application.CandidateId)
            .Select(c => c.FirstName + " " + c.LastName)
            .SingleOrDefaultAsync(cancellationToken) ?? "the candidate";

        // Pure-display title for task/notification text — not the advert-vs-profile distinction, so
        // resolved as AdvertTitle ?? PositionProfile.Title, same as the dashboard read features.
        var vacancyFields = await db.Vacancies
            .Where(v => v.Id == application.VacancyId)
            .Select(v => new { v.AdvertTitle, v.PositionProfileId })
            .SingleOrDefaultAsync(cancellationToken);

        var vacancyPositionProfile = vacancyFields is not null
            ? await positionProfileReader.GetSummaryAsync(request.CompanyId, vacancyFields.PositionProfileId, cancellationToken)
            : null;

        var vacancyTitle = vacancyFields?.AdvertTitle ?? vacancyPositionProfile?.Title ?? "this vacancy";

        var interviewDate = DateOnly.FromDateTime(request.ScheduledAt.UtcDateTime);

        await taskCreator.CreateAsync(
            request.CompanyId,
            createdBy:          scheduledBy,
            title:              $"Prepare for interview: {candidateName}",
            description:        $"Review {candidateName}'s application for the {vacancyTitle} vacancy ahead of the interview.",
            priority:           TaskPriority.Medium,
            source:             TaskSource.Recruitment,
            actionType:         TaskActionType.Review,
            dueDate:            interviewDate,
            assignedEmployeeId: request.InterviewerEmployeeId,
            assignedUserId:     request.InterviewerEmployeeId,
            sourceEntityId:     interview.Id,
            cancellationToken,
            notifyAssignee:     false);

        await taskCreator.CreateAsync(
            request.CompanyId,
            createdBy:          scheduledBy,
            title:              $"Provide feedback: interview with {candidateName}",
            description:        $"Record the outcome and feedback for {candidateName}'s interview for the {vacancyTitle} vacancy.",
            priority:           TaskPriority.Medium,
            source:             TaskSource.Recruitment,
            actionType:         TaskActionType.Complete,
            dueDate:            interviewDate.AddDays(1),
            assignedEmployeeId: request.InterviewerEmployeeId,
            assignedUserId:     request.InterviewerEmployeeId,
            sourceEntityId:     interview.Id,
            cancellationToken,
            notifyAssignee:     false);

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

        return Result.Success(new ScheduleInterviewResponse(
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
