using HR.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Features.GetUnreadNotificationCount;

internal sealed record GetUnreadNotificationCountResponse(int Count);

internal sealed class GetUnreadNotificationCountHandler(NotificationsDbContext dbContext)
{
    public async Task<GetUnreadNotificationCountResponse> HandleAsync(
        GetUnreadNotificationCountRequest request,
        CancellationToken cancellationToken)
    {
        var count = await dbContext.Notifications
            .CountAsync(
                n => n.CompanyId == request.CompanyId
                  && n.EmployeeId == request.EmployeeId
                  && !n.IsRead,
                cancellationToken);

        return new GetUnreadNotificationCountResponse(count);
    }
}
