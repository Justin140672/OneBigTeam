using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Sickness.Jobs;

internal sealed class SicknessEvidenceReminderJob(
    SicknessDbContext db,
    INotificationWriter notificationWriter,
    IIntegrationEventPublisher eventPublisher,
    IClock clock,
    ILogger<SicknessEvidenceReminderJob> logger)
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
                DetachQuietly(request);
                logger.LogError(
                    exception,
                    "Failed to mark fit-note evidence request {RequestId} overdue; continuing with the rest of the batch.",
                    request.Id);
            }
        }

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
