using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Notifications.Features.NotifyOnOrganisationDataExportCompleted;

/// <summary>
/// Story 2: raises an in-app notification for the requesting company administrator once their
/// organisation data export ZIP has finished building (OrganisationDataExportCompletedIntegrationEvent,
/// published by the Reporting build job). This is the "your download is ready" signal — the export
/// is prepared asynchronously and can take a while for a large organisation.
///
/// Recipient rule: the user who requested the export, carried on the event as RequestedByUserId.
/// INotificationWriter is keyed on employeeId; this module already relies on the platform-wide
/// convention that ApplicationUser.Id == Employee.Id (see HR.Modules.Identity — user_profiles and
/// user_roles are keyed on the employee id, and Identity's own OnEmployeeCreated handler treats
/// integrationEvent.EmployeeId as the user id). A company administrator is always a provisioned
/// employee, so RequestedByUserId is used directly as the notification's employeeId — no extra
/// cross-module lookup or new Abstractions reader is needed. If RequestedByUserId is null (export
/// requested by a system/automated path with no user), this is logged and nothing is written.
///
/// SourceEntityId is the ExportId so the notification is de-duplicated per export and the
/// NotificationActionRouteBuilder can route the click to the subscription page where the export
/// panel lives.
///
/// Idempotent: integration-event delivery may repeat, so ExistsAsync keyed on
/// (userId, exportId, OrganisationDataExportReady) is checked before writing.
/// </summary>
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
