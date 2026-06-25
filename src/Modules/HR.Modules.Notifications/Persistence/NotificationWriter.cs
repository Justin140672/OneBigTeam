using HR.Modules.Notifications.Domain;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Persistence;

internal sealed class NotificationWriter(NotificationsDbContext dbContext) : INotificationWriter
{
    public async Task WriteAsync(
        Guid id,
        Guid companyId,
        Guid employeeId,
        string title,
        string? body,
        Guid sourceEntityId,
        NotificationType type,
        NotificationPriority priority,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        var notification = Notification.Create(id, companyId, employeeId, title, body, sourceEntityId, createdAt, type, priority);
        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(
        Guid employeeId,
        Guid sourceEntityId,
        NotificationType type,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Notifications
            .AnyAsync(
                n => n.EmployeeId == employeeId && n.SourceEntityId == sourceEntityId && n.Type == type,
                cancellationToken);
    }
}
