using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Jobs;

/// <summary>
/// Notifies interviewers of upcoming interviews within the reminder window. Runs hourly (rather
/// than daily, like most other reminder jobs) because interviews are time-of-day specific — a
/// once-a-day run could either miss an interview entirely (if it runs after the interview's
/// window has already opened and closed) or fire the reminder many hours too early. Running
/// hourly with a 2-hour lookahead window guarantees every pending interview gets exactly one
/// reminder, roughly 1-2 hours ahead of the scheduled time.
/// </summary>
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
