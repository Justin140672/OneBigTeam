using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Jobs;

/// <summary>
/// Notifies hiring managers when interview feedback is overdue. An interview's feedback is
/// considered overdue once its scheduled time plus one day has passed and no outcome has been
/// recorded — mirroring the feedback task due date set by ScheduleInterviewHandler
/// (interviewDate.AddDays(1)). This is a self-contained signal derived entirely from Interview's
/// own ScheduledAt/Outcome, the same way AssetReminderJob computes overdue windows from its own
/// AssetAssignment timestamps rather than asking the Tasks module whether a task is overdue.
///
/// The hiring manager is resolved via Interview -> Application -> Vacancy -> HiringManagerId,
/// entirely within RecruitmentDbContext, since Vacancy is owned by this same module.
/// </summary>
internal sealed class OutstandingInterviewFeedbackReminderJob(
    RecruitmentDbContext db,
    INotificationWriter notificationWriter,
    IClock clock)
{
    public async Task ExecuteAsync()
    {
        var now = clock.UtcNowOffset();

        var overdueInterviews = await (
            from interview in db.Interviews.AsNoTracking()
            join application in db.Applications.AsNoTracking() on interview.ApplicationId equals application.Id
            join vacancy in db.Vacancies.AsNoTracking() on application.VacancyId equals vacancy.Id
            where interview.Outcome == InterviewOutcome.Pending &&
                  interview.ScheduledAt.AddDays(1) < now
            select new
            {
                interview.Id,
                interview.CompanyId,
                interview.ScheduledAt,
                vacancy.HiringManagerId,
            })
            .ToListAsync();

        foreach (var item in overdueInterviews)
        {
            var alreadySent = await notificationWriter.ExistsAsync(
                item.HiringManagerId, item.Id, NotificationType.InterviewFeedbackOverdue);

            if (alreadySent) continue;

            await notificationWriter.WriteAsync(
                Guid.NewGuid(),
                item.CompanyId,
                item.HiringManagerId,
                "Interview feedback overdue",
                $"Feedback for the interview scheduled on {item.ScheduledAt:d MMM yyyy 'at' HH:mm} has not yet been recorded.",
                item.Id,
                NotificationType.InterviewFeedbackOverdue,
                NotificationPriority.High,
                now);
        }
    }
}
