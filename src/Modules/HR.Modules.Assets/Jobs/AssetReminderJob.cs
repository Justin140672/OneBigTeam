using HR.Modules.Assets.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Jobs;

internal sealed class AssetReminderJob(
    AssetsDbContext db,
    INotificationWriter notificationWriter,
    IClock clock)
{
    private const int OverdueDays = 7;

    public async Task ExecuteAsync()
    {
        var now = clock.UtcNowOffset();

        await SendAcknowledgementOverdueAsync(now);
        await SendReturnOverdueAsync(now);
        await SendAcknowledgementRemindersAsync(now);
        await SendReturnRemindersAsync(now);
    }

    private async Task SendAcknowledgementRemindersAsync(DateTimeOffset now)
    {
        var unacknowledged = await db.AssetAssignments
            .AsNoTracking()
            .Where(a => a.AcknowledgedAt == null && a.ReturnedAt == null)
            .ToListAsync();

        foreach (var assignment in unacknowledged)
        {
            var alreadySent = await notificationWriter.ExistsAsync(
                assignment.EmployeeId, assignment.Id, NotificationType.AssetAcknowledgementReminder);

            if (alreadySent) continue;

            await notificationWriter.WriteAsync(
                Guid.NewGuid(),
                assignment.CompanyId,
                assignment.EmployeeId,
                "Reminder: please acknowledge your assigned asset",
                "You have an asset assigned to you that has not yet been acknowledged. Please confirm receipt.",
                assignment.Id,
                NotificationType.AssetAcknowledgementReminder,
                NotificationPriority.Normal,
                now);
        }
    }

    private async Task SendAcknowledgementOverdueAsync(DateTimeOffset now)
    {
        var cutoff = now.AddDays(-OverdueDays);

        var overdue = await db.AssetAssignments
            .AsNoTracking()
            .Where(a => a.AcknowledgedAt == null && a.ReturnedAt == null && a.AssignedAt <= cutoff)
            .ToListAsync();

        foreach (var assignment in overdue)
        {
            var alreadySent = await notificationWriter.ExistsAsync(
                assignment.EmployeeId, assignment.Id, NotificationType.AssetAcknowledgementOverdue);

            if (alreadySent) continue;

            await notificationWriter.WriteAsync(
                Guid.NewGuid(),
                assignment.CompanyId,
                assignment.EmployeeId,
                "Overdue: asset acknowledgement required",
                "Your assigned asset has not been acknowledged for over 7 days. Immediate action is required.",
                assignment.Id,
                NotificationType.AssetAcknowledgementOverdue,
                NotificationPriority.High,
                now);
        }
    }

    private async Task SendReturnOverdueAsync(DateTimeOffset now)
    {
        var activeAssignments = await db.AssetAssignments
            .AsNoTracking()
            .Where(a => a.ReturnedAt == null)
            .ToListAsync();

        foreach (var assignment in activeAssignments)
        {
            var reminderSent = await notificationWriter.ExistsAsync(
                assignment.EmployeeId, assignment.Id, NotificationType.AssetReturnReminder);

            if (!reminderSent) continue;

            var alreadySent = await notificationWriter.ExistsAsync(
                assignment.EmployeeId, assignment.Id, NotificationType.AssetReturnOverdue);

            if (alreadySent) continue;

            await notificationWriter.WriteAsync(
                Guid.NewGuid(),
                assignment.CompanyId,
                assignment.EmployeeId,
                "Overdue: asset return required",
                "A return has been requested for an asset assigned to you and remains overdue. Please return it immediately.",
                assignment.Id,
                NotificationType.AssetReturnOverdue,
                NotificationPriority.Urgent,
                now);
        }
    }

    private async Task SendReturnRemindersAsync(DateTimeOffset now)
    {
        var activeAssignments = await db.AssetAssignments
            .AsNoTracking()
            .Where(a => a.ReturnedAt == null)
            .ToListAsync();

        foreach (var assignment in activeAssignments)
        {
            var returnRequested = await notificationWriter.ExistsAsync(
                assignment.EmployeeId, assignment.Id, NotificationType.AssetReturnRequested);

            if (!returnRequested) continue;

            var reminderAlreadySent = await notificationWriter.ExistsAsync(
                assignment.EmployeeId, assignment.Id, NotificationType.AssetReturnReminder);

            if (reminderAlreadySent) continue;

            await notificationWriter.WriteAsync(
                Guid.NewGuid(),
                assignment.CompanyId,
                assignment.EmployeeId,
                "Reminder: please return your assigned asset",
                "A return has been requested for an asset assigned to you. Please return it as soon as possible.",
                assignment.Id,
                NotificationType.AssetReturnReminder,
                NotificationPriority.High,
                now);
        }
    }
}
