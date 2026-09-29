using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests.Infrastructure;

internal static class NotificationSeeder
{
    public static async Task<Guid> SeedAsync(
        ApiWebApplicationFactory factory,
        Guid companyId,
        Guid employeeId,
        string title = "Test notification",
        bool isRead = false,
        NotificationType type = NotificationType.TaskAssigned,
        NotificationPriority priority = NotificationPriority.Normal,
        DateTimeOffset? createdAt = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var notification = Notification.Create(
            Guid.NewGuid(), companyId, employeeId,
            title, null, Guid.NewGuid(),
            createdAt ?? DateTimeOffset.UtcNow,
            type, priority);

        if (isRead) notification.MarkAsRead();

        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        return notification.Id;
    }
}
