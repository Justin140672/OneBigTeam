using HR.Modules.Notifications.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Features.MarkNotificationRead;

internal sealed class MarkNotificationReadHandler(NotificationsDbContext dbContext)
{
    public async Task<Result> HandleAsync(MarkNotificationReadRequest request, CancellationToken cancellationToken)
    {
        var notification = await dbContext.Notifications
            .SingleOrDefaultAsync(
                n => n.Id == request.NotificationId && n.CompanyId == request.CompanyId,
                cancellationToken);

        if (notification is null)
            return Result.Failure(Error.NotFound($"Notification '{request.NotificationId}' was not found."));

        notification.MarkAsRead();
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
