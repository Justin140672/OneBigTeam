using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
/// rolls a committed transition back. A save failure for one request only detaches that request from
/// the change tracker (not the whole batch — see OBT-REM-10 below), so later requests in the same
/// batch still transition and persist correctly.
/// </para>
/// <para>
/// OBT-REM-10: the overdue notification and the overdue integration event are tracked with two
/// independent, durable progress markers (<see cref="SicknessEvidenceRequest.OverdueNotifiedAt"/>
/// and <see cref="SicknessEvidenceRequest.OverdueEventPublishedAt"/>), each committed immediately
/// after the corresponding side effect succeeds. This means a retry repairs exactly the work that
/// did not finish — e.g. if the notification was written but publishing the event then failed, a
/// retry finds <c>OverdueNotifiedAt</c> already set (skips re-notifying) and
/// <c>OverdueEventPublishedAt</c> still null (publishes the missing event), instead of skipping the
/// whole block. The event itself carries a deterministic identity — the evidence request id — a
/// request can only transition Pending → Overdue once (Reschedule resets the markers if it is ever
/// re-anchored into the future), so consumers can safely treat the request id as a natural
/// idempotency key. Every consumer of <c>SicknessEvidenceOverdueIntegrationEvent</c> must be
/// idempotent for that identity (e.g. the Tasks module's overdue-fit-note handler checks for an
/// existing open task for the source entity before creating another).
/// One failing employee is logged and skipped without blocking the rest of the batch.
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
                // OBT-REM-10: detach only the failed entity. Clearing the whole change tracker
                // here would also detach every other still-pending item already loaded into
                // `newlyOverdue`, silently preventing their MarkOverdue() calls from being
                // persisted on later iterations of this same loop.
                DetachQuietly(request);
                logger.LogError(
                    exception,
                    "Failed to mark fit-note evidence request {RequestId} overdue; continuing with the rest of the batch.",
                    request.Id);
            }
        }

        // Step 2: reconcile the overdue notification and overdue event for every recently-overdue
        // request, each guarded by its own durable progress marker so the two are repaired
        // independently.
        var reconcileFrom = today.AddDays(-OverdueReconciliationDays);

        var overdue = await (
            from request in db.SicknessEvidenceRequests
            join record in db.SicknessRecords on request.SicknessRecordId equals record.Id
            where request.Status == SicknessEvidenceRequestStatus.Overdue &&
                  request.DueDate >= reconcileFrom &&
                  request.DueDate < today &&
                  (request.OverdueNotifiedAt == null || request.OverdueEventPublishedAt == null)
            select new
            {
                Request = request,
                record.EmployeeId,
            })
            .ToListAsync(cancellationToken);

        foreach (var item in overdue)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var request = item.Request;

            try
            {
                if (request.OverdueNotifiedAt is null)
                {
                    await notificationWriter.WriteAsync(
                        Guid.NewGuid(),
                        request.CompanyId,
                        item.EmployeeId,
                        "Overdue: fit note evidence required",
                        "Your fit note evidence request is now overdue. Please upload the required document as soon as possible.",
                        request.Id,
                        NotificationType.SicknessEvidenceOverdue,
                        NotificationPriority.High,
                        now,
                        cancellationToken);

                    request.MarkOverdueNotified(now);
                    await db.SaveChangesAsync(cancellationToken);
                }

                if (request.OverdueEventPublishedAt is null)
                {
                    // Deterministic identity: a request transitions Pending -> Overdue at most
                    // once (Reschedule resets both markers if it is ever re-anchored into the
                    // future), so the request id itself is a stable, natural idempotency key for
                    // every consumer of this event.
                    await eventPublisher.PublishAsync(new SicknessEvidenceOverdueIntegrationEvent(
                        request.CompanyId,
                        item.EmployeeId,
                        request.SicknessRecordId,
                        request.Id,
                        request.DueDate,
                        now), cancellationToken);

                    request.MarkOverdueEventPublished(now);
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                DetachQuietly(request);
                logger.LogError(
                    exception,
                    "Failed to reconcile overdue fit-note evidence notification/event for request {RequestId}; continuing with the rest of the batch.",
                    request.Id);
            }
        }
    }

    private void DetachQuietly(SicknessEvidenceRequest request)
    {
        var entry = db.Entry(request);
        if (entry.State != EntityState.Detached)
            entry.State = EntityState.Detached;
    }
}
