using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ScheduleInterview;

internal sealed class ScheduleInterviewHandler(RecruitmentDbContext db, IClock clock)
{
    public async Task<Result<ScheduleInterviewResponse>> HandleAsync(
        ScheduleInterviewRequest request,
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

        switch (application.Status)
        {
            case ApplicationStatus.Applied or ApplicationStatus.Screening:
                application.ScheduleInterview(now);
                break;
            case ApplicationStatus.InterviewScheduled:
                // Additional interview round for an application already in progress.
                break;
            default:
                return Result.Failure<ScheduleInterviewResponse>(
                    Error.Validation($"Cannot schedule an interview for an application with status '{application.Status}'."));
        }

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
