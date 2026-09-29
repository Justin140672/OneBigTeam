using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Notifications.Features.NotifyOnOrganisationDataExportCompleted;

internal sealed class NotifyOnOrganisationDataExportCompletedHandler(
    INotificationWriter notificationWriter,
    ILogger<NotifyOnOrganisationDataExportCompletedHandler> logger)
    : IIntegrationEventHandler<OrganisationDataExportCompletedIntegrationEvent>
{
    public async Task HandleAsync(OrganisationDataExportCompletedIntegrationEvent e, CancellationToken cancellationToken)
    {
        if (e.RequestedByUserId is not { } userId)
        {
            logger.LogWarning(
                "Skipping OrganisationDataExportReady notification for export {ExportId} in company {CompanyId}: the export has no requesting user to notify.",
                e.ExportId, e.CompanyId);
            return;
        }

        var alreadySent = await notificationWriter.ExistsAsync(
            userId, e.ExportId, NotificationType.OrganisationDataExportReady, cancellationToken);
        if (alreadySent)
            return;

        await notificationWriter.WriteAsync(
            Guid.NewGuid(),
            e.CompanyId,
            userId,
            "Your organisation data export is ready",
            "The full export of your organisation's data has finished building and can now be downloaded from the Subscription page. The download will remain available for 7 days.",
            e.ExportId,
            NotificationType.OrganisationDataExportReady,
            NotificationPriority.Normal,
            e.CompletedAt,
            cancellationToken);
    }
}
