using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Features.MarkAllNotificationsRead;

internal sealed class MarkAllNotificationsReadHandler(
    NotificationsDbContext dbContext,
    IAuditEventPublisher auditPublisher,
    IClock clock)
{
    public async Task HandleAsync(MarkAllNotificationsReadRequest request, CancellationToken cancellationToken)
    {
        var unread = await dbContext.Notifications
            .Where(n => n.CompanyId == request.CompanyId
                     && n.EmployeeId == request.EmployeeId
                     && !n.IsRead)
            .ToListAsync(cancellationToken);

        if (unread.Count == 0)
            return;

        foreach (var n in unread)
            n.MarkAsRead();

        await dbContext.SaveChangesAsync(cancellationToken);

        // NOT-05: one event per notification transitioned, mirroring the per-entity audit
        // convention used elsewhere in this session rather than a single aggregated "N marked
        // read" event. Nothing is published above when there was nothing unread to begin with.
        var now = clock.UtcNowOffset();
        foreach (var n in unread)
        {
            await auditPublisher.PublishAsync(new NotificationReadAuditEvent(
                request.CompanyId, n.Id, request.EmployeeId, now), cancellationToken);
        }
    }
}
