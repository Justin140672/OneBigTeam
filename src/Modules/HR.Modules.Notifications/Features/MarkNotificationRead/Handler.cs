using HR.Modules.Notifications.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Features.MarkNotificationRead;

internal sealed class MarkNotificationReadHandler(NotificationsDbContext dbContext)
{
    public async Task<Result> HandleAsync(MarkNotificationReadRequest request, CancellationToken cancellationToken)
    {
        // NOT-01: recipient must be included in the lookup itself (not checked after the fact) so
        // that a notification owned by a different employee is indistinguishable from one that
        // doesn't exist — same anti-enumeration convention used across the other resource
        // authorizers this session (e.g. DOC-01).
        var notification = await dbContext.Notifications
            .SingleOrDefaultAsync(
                n => n.Id == request.NotificationId
                     && n.CompanyId == request.CompanyId
                     && n.EmployeeId == request.EmployeeId,
                cancellationToken);

        if (notification is null)
            return Result.Failure(Error.NotFound($"Notification '{request.NotificationId}' was not found."));

        notification.MarkAsRead();
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
