using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Sickness.Jobs;

/// <summary>
/// Sends fit-note evidence reminders as a request approaches its due date, and marks it overdue
/// (with a further, higher-priority notification and an integration event) once the due date has
/// passed.
///
/// <para>
/// OBT-REM-04: retry-safe. The <c>Pending → Overdue</c> status transition is committed per request
/// <b>before</b> any notification is written or event is published, so a mid-batch failure never
/// rolls a committed transition back. The overdue notification/event step then runs against every
/// recently-overdue request guarded by a durable idempotency key
/// (<c>ExistsAsync(employee, requestId, SicknessEvidenceOverdue)</c>), so a Hangfire retry completes
/// only the work that did not finish — it never re-notifies or re-publishes. One failing employee is
/// logged and skipped without blocking the rest of the batch.
/// </para>
/// </summary>
internal sealed class SicknessEvidenceReminderJob(
    SicknessDbContext db,
    INotificationWriter notificationWriter,
    IIntegrationEventPublisher eventPublisher,
    IClock clock,
    ILogger<SicknessEvidenceReminderJob> logger)
{
    private const int ReminderWindowDays = 2;

    // How far back to keep reconciling missing overdue notifications after the status transition.
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

        var pendingRequests = await (
            from request in db.SicknessEvidenceRequests
            join record in db.SicknessRecords on request.SicknessRecordId equals record.Id
            where request.Status == SicknessEvidenceRequestStatus.Pending &&
                  request.DueDate >= today &&
                  request.DueDate <= reminderCutoff
            select new { request.Id, request.CompanyId, request.DueDate, record.EmployeeId })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var item in pendingRequests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var alreadySent = await notificationWriter.ExistsAsync(
                    item.EmployeeId, item.Id, NotificationType.SicknessEvidenceReminder, cancellationToken);

                if (alreadySent) continue;

                await notificationWriter.WriteAsync(
                    Guid.NewGuid(),
                    item.CompanyId,
                    item.EmployeeId,
                    "Reminder: fit note evidence required",
                    "You have a fit note evidence request that is due soon. Please upload the required document.",
                    item.Id,
                    NotificationType.SicknessEvidenceReminder,
                    NotificationPriority.Normal,
                    now,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(
                    exception,
                    "Failed to send fit-note evidence reminder for request {RequestId}; continuing with the rest of the batch.",
                    item.Id);
            }
        }
    }

    private async Task MarkOverdueAndNotifyAsync(DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Step 1: durably transition each newly-overdue request. Commit per request so one failure
        // does not undo earlier transitions and a retry resumes from where it stopped.
        var newlyOverdue = await db.SicknessEvidenceRequests
            .Where(r => r.Status == SicknessEvidenceRequestStatus.Pending && r.DueDate < today)
            .ToListAsync(cancellationToken);

        foreach (var request in newlyOverdue)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                request.MarkOverdue(now);
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                logger.LogError(
                    exception,
                    "Failed to mark fit-note evidence request {RequestId} overdue; continuing with the rest of the batch.",
                    request.Id);
            }
        }

        // Step 2: reconcile the overdue notification/event for every recently-overdue request.
        // Guarded by a durable idempotency key so a retry only fills gaps.
        var reconcileFrom = today.AddDays(-OverdueReconciliationDays);

        var overdue = await (
            from request in db.SicknessEvidenceRequests
            join record in db.SicknessRecords on request.SicknessRecordId equals record.Id
            where request.Status == SicknessEvidenceRequestStatus.Overdue &&
                  request.DueDate >= reconcileFrom &&
                  request.DueDate < today
            select new
            {
                request.Id,
                request.CompanyId,
                request.SicknessRecordId,
                request.DueDate,
                record.EmployeeId,
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var item in overdue)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var alreadyNotified = await notificationWriter.ExistsAsync(
                    item.EmployeeId, item.Id, NotificationType.SicknessEvidenceOverdue, cancellationToken);

                if (alreadyNotified) continue;

                await notificationWriter.WriteAsync(
                    Guid.NewGuid(),
                    item.CompanyId,
                    item.EmployeeId,
                    "Overdue: fit note evidence required",
                    "Your fit note evidence request is now overdue. Please upload the required document as soon as possible.",
                    item.Id,
                    NotificationType.SicknessEvidenceOverdue,
                    NotificationPriority.High,
                    now,
                    cancellationToken);

                await eventPublisher.PublishAsync(new SicknessEvidenceOverdueIntegrationEvent(
                    item.CompanyId,
                    item.EmployeeId,
                    item.SicknessRecordId,
                    item.Id,
                    item.DueDate,
                    now), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(
                    exception,
                    "Failed to send overdue fit-note evidence notification for request {RequestId}; continuing with the rest of the batch.",
                    item.Id);
            }
        }
    }
}
