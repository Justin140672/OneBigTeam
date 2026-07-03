using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Jobs;

/// <summary>
/// Sends the employee's manager a reminder as a return-to-work review approaches its due
/// date, and marks it overdue (with a further notification) once the due date has passed.
/// Mirrors SicknessEvidenceReminderJob's shape. Unlike that job, the notification recipient
/// is the employee's manager (via IManagerReader) — the review's own EmployeeId is the
/// employee being reviewed, not the manager conducting the review.
/// </summary>
internal sealed class ReturnToWorkReminderJob(
    SicknessDbContext db,
    INotificationWriter notificationWriter,
    IManagerReader managerReader,
    IClock clock)
{
    private const int ReminderWindowDays = 2;

    public async Task ExecuteAsync()
    {
        var now = clock.UtcNowOffset();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        await SendRemindersAsync(today, now);
        await MarkOverdueAndNotifyAsync(today, now);
    }

    private async Task SendRemindersAsync(DateOnly today, DateTimeOffset now)
    {
        var reminderCutoff = today.AddDays(ReminderWindowDays);

        var dueSoon = await db.ReturnToWorkReviews
            .Where(r => r.Status == ReturnToWorkReviewStatus.Pending &&
                        r.DueDate >= today &&
                        r.DueDate <= reminderCutoff)
            .ToListAsync();

        foreach (var review in dueSoon)
        {
            var managerId = await managerReader.GetManagerIdAsync(review.CompanyId, review.EmployeeId, CancellationToken.None);
            if (managerId is null) continue;

            var alreadySent = await notificationWriter.ExistsAsync(
                managerId.Value, review.Id, NotificationType.ReturnToWorkReviewReminder);

            if (alreadySent) continue;

            await notificationWriter.WriteAsync(
                Guid.NewGuid(),
                review.CompanyId,
                managerId.Value,
                "Reminder: return-to-work review due soon",
                "A return-to-work review you're responsible for is due soon. Please complete it.",
                review.Id,
                NotificationType.ReturnToWorkReviewReminder,
                NotificationPriority.Normal,
                now);
        }
    }

    private async Task MarkOverdueAndNotifyAsync(DateOnly today, DateTimeOffset now)
    {
        var overdueReviews = await db.ReturnToWorkReviews
            .Where(r => r.Status == ReturnToWorkReviewStatus.Pending && r.DueDate < today)
            .ToListAsync();

        var changed = false;

        foreach (var review in overdueReviews)
        {
            var managerId = await managerReader.GetManagerIdAsync(review.CompanyId, review.EmployeeId, CancellationToken.None);

            review.MarkOverdue(now);
            changed = true;

            if (managerId is null) continue;

            await notificationWriter.WriteAsync(
                Guid.NewGuid(),
                review.CompanyId,
                managerId.Value,
                "Overdue: return-to-work review required",
                "A return-to-work review you're responsible for is now overdue. Please complete it as soon as possible.",
                review.Id,
                NotificationType.ReturnToWorkReviewOverdue,
                NotificationPriority.High,
                now);
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }
}
