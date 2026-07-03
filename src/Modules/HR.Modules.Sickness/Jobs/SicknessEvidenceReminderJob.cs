using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Jobs;

internal sealed class SicknessEvidenceReminderJob(
    SicknessDbContext db,
    INotificationWriter notificationWriter,
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

        var pendingRequests = await (
            from request in db.SicknessEvidenceRequests
            join record in db.SicknessRecords on request.SicknessRecordId equals record.Id
            where request.Status == SicknessEvidenceRequestStatus.Pending &&
                  request.DueDate >= today &&
                  request.DueDate <= reminderCutoff
            select new { request.Id, request.CompanyId, request.DueDate, record.EmployeeId })
            .AsNoTracking()
            .ToListAsync();

        foreach (var item in pendingRequests)
        {
            var alreadySent = await notificationWriter.ExistsAsync(
                item.EmployeeId, item.Id, NotificationType.SicknessEvidenceReminder);

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
                now);
        }
    }

    private async Task MarkOverdueAndNotifyAsync(DateOnly today, DateTimeOffset now)
    {
        var overdueRequests = await (
            from request in db.SicknessEvidenceRequests
            join record in db.SicknessRecords on request.SicknessRecordId equals record.Id
            where request.Status == SicknessEvidenceRequestStatus.Pending &&
                  request.DueDate < today
            select new { Request = request, record.EmployeeId })
            .ToListAsync();

        foreach (var item in overdueRequests)
        {
            item.Request.MarkOverdue(now);

            await notificationWriter.WriteAsync(
                Guid.NewGuid(),
                item.Request.CompanyId,
                item.EmployeeId,
                "Overdue: fit note evidence required",
                "Your fit note evidence request is now overdue. Please upload the required document as soon as possible.",
                item.Request.Id,
                NotificationType.SicknessEvidenceOverdue,
                NotificationPriority.High,
                now);
        }

        if (overdueRequests.Count > 0)
        {
            await db.SaveChangesAsync();
        }
    }
}
