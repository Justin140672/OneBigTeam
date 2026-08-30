using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Features.GetAdministrativeAlertUnreadCount;

internal sealed record GetAdministrativeAlertUnreadCountResponse(int Count);

internal sealed class GetAdministrativeAlertUnreadCountHandler(NotificationsDbContext dbContext)
{
    public async Task<GetAdministrativeAlertUnreadCountResponse> HandleAsync(
        GetAdministrativeAlertUnreadCountRequest request,
        CancellationToken cancellationToken)
    {
        var count = await dbContext.AdministrativeAlerts
            .AsNoTracking()
            .CountAsync(
                a => a.CompanyId == request.CompanyId
                     && !a.IsRead
                     && a.Status != AdministrativeAlertStatus.Resolved,
                cancellationToken);

        return new GetAdministrativeAlertUnreadCountResponse(count);
    }
}
