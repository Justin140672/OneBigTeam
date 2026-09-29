using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Jobs;

internal sealed class InterviewReminderJob(
    RecruitmentDbContext db,
    INotificationWriter notificationWriter,
    IClock clock)
{
    private static readonly TimeSpan ReminderWindow = TimeSpan.FromHours(2);

    public async Task ExecuteAsync()
    {
        var now = clock.UtcNowOffset();
        var windowEnd = now.Add(ReminderWindow);

        var upcomingInterviews = await db.Interviews
            .AsNoTracking()
            .Where(i => i.Outcome == InterviewOutcome.Pending &&
                        i.ScheduledAt >= now &&
                        i.ScheduledAt <= windowEnd)
            .ToListAsync();

        foreach (var interview in upcomingInterviews)
        {
            var alreadySent = await notificationWriter.ExistsAsync(
                interview.InterviewerEmployeeId, interview.Id, NotificationType.InterviewReminder);

            if (alreadySent) continue;

            await notificationWriter.WriteAsync(
                Guid.NewGuid(),
                interview.CompanyId,
                interview.InterviewerEmployeeId,
                "Reminder: upcoming interview",
                $"You have an interview scheduled at {interview.ScheduledAt:d MMM yyyy 'at' HH:mm}.",
                interview.Id,
                NotificationType.InterviewReminder,
                NotificationPriority.Normal,
                now);
        }
    }
}
