using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Features.MarkNotificationRead;

internal sealed class MarkNotificationReadHandler(
    NotificationsDbContext dbContext,
    IAuditEventPublisher auditPublisher,
    IClock clock)
{
    public async Task<Result> HandleAsync(MarkNotificationReadRequest request, CancellationToken cancellationToken)
    {
        var notification = await dbContext.Notifications
            .SingleOrDefaultAsync(
                n => n.Id == request.NotificationId
                     && n.CompanyId == request.CompanyId
                     && n.EmployeeId == request.EmployeeId,
                cancellationToken);

        if (notification is null)
            return Result.Failure(Error.NotFound($"Notification '{request.NotificationId}' was not found."));

        var wasUnread = !notification.IsRead;

        notification.MarkAsRead();
        await dbContext.SaveChangesAsync(cancellationToken);

        if (wasUnread)
        {
            await auditPublisher.PublishAsync(new NotificationReadAuditEvent(
                request.CompanyId, notification.Id, request.EmployeeId, clock.UtcNowOffset()), cancellationToken);
        }

        return Result.Success();
    }
}
