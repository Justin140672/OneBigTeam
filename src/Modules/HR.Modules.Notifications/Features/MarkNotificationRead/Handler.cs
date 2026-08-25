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

        // NOT-05: only audit an actual unread-to-read transition — re-marking an already-read
        // notification (idempotent no-op, see the existing test coverage for this handler) must
        // not raise a second, misleading read event.
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
