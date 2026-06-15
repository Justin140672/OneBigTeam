using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Features.ListNotifications;

internal sealed class ListNotificationsHandler(TasksDbContext dbContext)
{
    public async Task<ListNotificationsResponse> HandleAsync(
        ListNotificationsRequest request,
        CancellationToken cancellationToken)
    {
        var items = await dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.CompanyId == request.CompanyId && n.EmployeeId == request.EmployeeId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .Select(n => new NotificationItem(n.Id, n.Title, n.Body, n.IsRead, n.SourceEntityId, n.CreatedAt))
            .ToListAsync(cancellationToken);

        var unreadCount = items.Count(n => !n.IsRead);

        return new ListNotificationsResponse(unreadCount, items);
    }
}
