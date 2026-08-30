using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Features.ResolveAdministrativeAlert;

internal enum ResolveAdministrativeAlertOutcome
{
    Resolved,
    NotFound,
    Conflict,
}

internal sealed class ResolveAdministrativeAlertHandler(
    NotificationsDbContext dbContext,
    IAuditEventPublisher auditPublisher,
    IClock clock)
{
    public async Task<ResolveAdministrativeAlertOutcome> HandleAsync(
        ResolveAdministrativeAlertRequest request,
        CancellationToken cancellationToken)
    {
        var alert = await dbContext.AdministrativeAlerts
            .SingleOrDefaultAsync(
                a => a.Id == request.AlertId && a.CompanyId == request.CompanyId,
                cancellationToken);

        if (alert is null)
            return ResolveAdministrativeAlertOutcome.NotFound;

        if (alert.Status == AdministrativeAlertStatus.Resolved)
            return ResolveAdministrativeAlertOutcome.Conflict;

        var now = clock.UtcNowOffset();
        alert.Resolve(request.ActorUserId, request.ResolutionNote, now);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(
            new AdministrativeAlertResolvedAuditEvent(
                request.CompanyId, alert.Id, request.ActorUserId, request.ResolutionNote, now),
            cancellationToken);

        return ResolveAdministrativeAlertOutcome.Resolved;
    }
}
