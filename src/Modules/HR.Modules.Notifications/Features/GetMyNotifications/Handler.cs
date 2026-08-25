using HR.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Features.GetMyNotifications;

internal sealed class GetMyNotificationsHandler(NotificationsDbContext dbContext)
{
    public async Task<GetMyNotificationsResponse> HandleAsync(
        GetMyNotificationsRequest request,
        CancellationToken cancellationToken)
    {
        var items = await dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.CompanyId == request.CompanyId && n.EmployeeId == request.EmployeeId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .Select(n => new MyNotificationItem(
                n.Id, n.Title, n.Body, n.IsRead,
                n.SourceEntityId, n.Type.ToString(), n.Priority.ToString(), n.CreatedAt, n.ActionUrl))
            .ToListAsync(cancellationToken);

        var unreadCount = items.Count(n => !n.IsRead);

        return new GetMyNotificationsResponse(unreadCount, items);
    }
}
