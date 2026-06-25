using HR.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Features.MarkAllNotificationsRead;

internal sealed class MarkAllNotificationsReadHandler(NotificationsDbContext dbContext)
{
    public async Task HandleAsync(MarkAllNotificationsReadRequest request, CancellationToken cancellationToken)
    {
        var unread = await dbContext.Notifications
            .Where(n => n.CompanyId == request.CompanyId
                     && n.EmployeeId == request.EmployeeId
                     && !n.IsRead)
            .ToListAsync(cancellationToken);

        foreach (var n in unread)
            n.MarkAsRead();

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
