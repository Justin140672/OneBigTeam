using HR.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Features.GetMyNotifications;

internal sealed class GetMyNotificationsHandler(NotificationsDbContext dbContext)
{
    public async Task<GetMyNotificationsResponse> HandleAsync(
        GetMyNotificationsRequest request,
        CancellationToken cancellationToken)
    {
        // NOT-06: unread total is a genuinely independent query — unfiltered (ignores IsRead/Type/
        // Priority/date-range filters below) and unpaginated, so it always represents every unread
        // notification belonging to the employee, never a count derived from the current page.
        var unreadCount = await dbContext.Notifications
            .AsNoTracking()
            .CountAsync(
                n => n.CompanyId == request.CompanyId
                     && n.EmployeeId == request.EmployeeId
                     && !n.IsRead,
                cancellationToken);

        var query = dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.CompanyId == request.CompanyId && n.EmployeeId == request.EmployeeId);

        if (request.IsRead is not null)
            query = query.Where(n => n.IsRead == request.IsRead.Value);

        if (request.Type is not null)
            query = query.Where(n => n.Type == request.Type.Value);

        if (request.Priority is not null)
            query = query.Where(n => n.Priority == request.Priority.Value);

        if (request.CreatedFrom is not null)
            query = query.Where(n => n.CreatedAt >= request.CreatedFrom.Value);

        if (request.CreatedTo is not null)
            query = query.Where(n => n.CreatedAt <= request.CreatedTo.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var pageSize = request.PageSize <= 0 ? 50 : request.PageSize;
        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;

        // NOT-06: deterministic ordering — CreatedAt DESC, Id DESC as tie-break so notifications
        // created with equal timestamps (concurrent writes) still page/order deterministically
        // instead of depending on unspecified database row order.
        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new MyNotificationItem(
                n.Id, n.Title, n.Body, n.IsRead,
                n.SourceEntityId, n.Type.ToString(), n.Priority.ToString(), n.CreatedAt, n.ActionUrl))
            .ToListAsync(cancellationToken);

        var totalPages = pageSize == 0 ? 0 : (int)Math.Ceiling((double)totalCount / pageSize);

        return new GetMyNotificationsResponse(unreadCount, items, totalCount, pageNumber, pageSize, totalPages);
    }
}
