using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ScheduleInterview;

internal sealed class ScheduleInterviewHandler(
    RecruitmentDbContext db,
    ITaskCreator taskCreator,
    INotificationWriter notificationWriter,
    IClock clock,
    IPositionProfileReader positionProfileReader,
    RecruitmentStageChangeRecorder recorder)
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

        var now = clock.UtcNowOffset();
        var previousStatus = application.Status;
        var stageChanged = false;

        switch (application.Status)
        {
            case ApplicationStatus.Applied or ApplicationStatus.Screening:
                application.ScheduleInterview(now);
                stageChanged = true;
                break;
            case ApplicationStatus.InterviewScheduled:
                // Additional interview round for an application already in progress — no stage
                // change, so no stage-history entry/integration event/audit event is recorded here.
                break;
            default:
                return Result.Failure<ScheduleInterviewResponse>(
                    Error.Validation($"Cannot schedule an interview for an application with status '{application.Status}'."));
        }

        if (stageChanged)
            recorder.AddHistoryEntry(application, previousStatus, scheduledBy, now);

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

        if (stageChanged)
            await recorder.PublishStageChangedEventsAsync(application, previousStatus, scheduledBy, now, cancellationToken);

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
