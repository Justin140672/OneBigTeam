using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Features.AcknowledgeAdministrativeAlert;

internal enum AcknowledgeAdministrativeAlertOutcome
{
    Acknowledged,
    NotFound,
    Conflict,
}

internal sealed class AcknowledgeAdministrativeAlertHandler(
    NotificationsDbContext dbContext,
    IAuditEventPublisher auditPublisher,
    IClock clock)
{
    public async Task<AcknowledgeAdministrativeAlertOutcome> HandleAsync(
        AcknowledgeAdministrativeAlertRequest request,
        CancellationToken cancellationToken)
    {
        var alert = await dbContext.AdministrativeAlerts
            .SingleOrDefaultAsync(
                a => a.Id == request.AlertId && a.CompanyId == request.CompanyId,
                cancellationToken);

        if (alert is null)
            return AcknowledgeAdministrativeAlertOutcome.NotFound;

        if (alert.Status != AdministrativeAlertStatus.Open)
            return AcknowledgeAdministrativeAlertOutcome.Conflict;

        var now = clock.UtcNowOffset();
        alert.Acknowledge(request.ActorUserId, now);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(
            new AdministrativeAlertAcknowledgedAuditEvent(request.CompanyId, alert.Id, request.ActorUserId, now),
            cancellationToken);

        return AcknowledgeAdministrativeAlertOutcome.Acknowledged;
    }
}
