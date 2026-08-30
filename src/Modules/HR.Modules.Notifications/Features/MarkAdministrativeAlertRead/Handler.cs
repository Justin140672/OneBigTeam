using HR.Modules.Notifications.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Features.MarkAdministrativeAlertRead;

internal sealed class MarkAdministrativeAlertReadHandler(NotificationsDbContext dbContext)
{
    public async Task<Result> HandleAsync(MarkAdministrativeAlertReadRequest request, CancellationToken cancellationToken)
    {
        // ADM-03: company scoped into the lookup itself — an alert from another company is
        // indistinguishable from a missing one (anti-enumeration).
        var alert = await dbContext.AdministrativeAlerts
            .SingleOrDefaultAsync(
                a => a.Id == request.AlertId && a.CompanyId == request.CompanyId,
                cancellationToken);

        if (alert is null)
            return Result.Failure(Error.NotFound($"Administrative alert '{request.AlertId}' was not found."));

        // ADM-03: marking read is not audited — acknowledge/resolve are the audited state changes.
        alert.MarkAsRead();
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
