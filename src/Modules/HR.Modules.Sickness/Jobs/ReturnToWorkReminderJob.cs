using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Sickness.Jobs;

/// <summary>
/// Sends the employee's manager a reminder as a return-to-work review approaches its due
/// date, and marks it overdue (with a further notification) once the due date has passed.
/// Mirrors SicknessEvidenceReminderJob's shape. Unlike that job, the notification recipient
/// is the employee's manager (via IManagerReader) — the review's own EmployeeId is the
/// employee being reviewed, not the manager conducting the review.
///
/// <para>
/// OBT-REM-04: retry-safe. The <c>Pending → Overdue</c> transition is committed per review before
/// any notification is written, and the overdue notification is then reconciled against every
/// recently-overdue review using a durable idempotency key
/// (<c>ExistsAsync(manager, reviewId, ReturnToWorkReviewOverdue)</c>). A Hangfire retry completes
/// only the missing work; one failing employee is logged and skipped.
/// </para>
/// <para>
/// OBT-REM-10: a save failure for one review only detaches that review from the change tracker
/// (not the whole batch), so later reviews in the same run still transition and persist correctly.
/// </para>
/// </summary>
internal sealed class ReturnToWorkReminderJob(
    SicknessDbContext db,
    INotificationWriter notificationWriter,
    IManagerReader managerReader,
    IClock clock,
    ILogger<ReturnToWorkReminderJob> logger)
{
    private const int ReminderWindowDays = 2;
    private const int OverdueReconciliationDays = 30;

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNowOffset();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        await SendRemindersAsync(today, now, cancellationToken);
        await MarkOverdueAndNotifyAsync(today, now, cancellationToken);
    }

    private async Task SendRemindersAsync(DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var reminderCutoff = today.AddDays(ReminderWindowDays);

        var dueSoon = await db.ReturnToWorkReviews
            .AsNoTracking()
            .Where(r => r.Status == ReturnToWorkReviewStatus.Pending &&
                        r.DueDate >= today &&
                        r.DueDate <= reminderCutoff)
            .ToListAsync(cancellationToken);

        foreach (var review in dueSoon)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var managerId = await managerReader.GetManagerIdAsync(review.CompanyId, review.EmployeeId, cancellationToken);
                if (managerId is null) continue;

                var alreadySent = await notificationWriter.ExistsAsync(
                    managerId.Value, review.Id, NotificationType.ReturnToWorkReviewReminder, cancellationToken);

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
                    now,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(
                    exception,
                    "Failed to send return-to-work reminder for review {ReviewId}; continuing with the rest of the batch.",
                    review.Id);
            }
        }
    }

    private async Task MarkOverdueAndNotifyAsync(DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Step 1: durably transition each newly-overdue review, committing per review.
        var newlyOverdue = await db.ReturnToWorkReviews
            .Where(r => r.Status == ReturnToWorkReviewStatus.Pending && r.DueDate < today)
            .ToListAsync(cancellationToken);

        foreach (var review in newlyOverdue)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                review.MarkOverdue(now);
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // OBT-REM-10: detach only the failed entity — clearing the whole change tracker
                // would also detach every other still-pending review already loaded into
                // `newlyOverdue`, silently preventing their MarkOverdue() calls from persisting.
                var entry = db.Entry(review);
                if (entry.State != EntityState.Detached)
                    entry.State = EntityState.Detached;

                logger.LogError(
                    exception,
                    "Failed to mark return-to-work review {ReviewId} overdue; continuing with the rest of the batch.",
                    review.Id);
            }
        }

        // Step 2: reconcile the overdue notification for every recently-overdue review.
        var reconcileFrom = today.AddDays(-OverdueReconciliationDays);

        var overdue = await db.ReturnToWorkReviews
            .AsNoTracking()
            .Where(r => r.Status == ReturnToWorkReviewStatus.Overdue &&
                        r.DueDate >= reconcileFrom &&
                        r.DueDate < today)
            .ToListAsync(cancellationToken);

        foreach (var review in overdue)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var managerId = await managerReader.GetManagerIdAsync(review.CompanyId, review.EmployeeId, cancellationToken);
                if (managerId is null) continue;

                var alreadyNotified = await notificationWriter.ExistsAsync(
                    managerId.Value, review.Id, NotificationType.ReturnToWorkReviewOverdue, cancellationToken);

                if (alreadyNotified) continue;

                await notificationWriter.WriteAsync(
                    Guid.NewGuid(),
                    review.CompanyId,
                    managerId.Value,
                    "Overdue: return-to-work review required",
                    "A return-to-work review you're responsible for is now overdue. Please complete it as soon as possible.",
                    review.Id,
                    NotificationType.ReturnToWorkReviewOverdue,
                    NotificationPriority.High,
                    now,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(
                    exception,
                    "Failed to send overdue return-to-work notification for review {ReviewId}; continuing with the rest of the batch.",
                    review.Id);
            }
        }
    }
}
