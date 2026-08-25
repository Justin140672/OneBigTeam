using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Notifications.Features.NotifyOnEmployeeCreated;

/// <summary>
/// NOT-07: raises an EmployeeCreated notification when a new employee is added. Before this
/// handler, EmployeeCreatedIntegrationEvent was consumed by Probation, Leave, Onboarding, Documents,
/// Assets and Employees' own timeline projection, but nothing in the app ever raised an in-app/email
/// notification for it — EmployeeCreated has had a registered NotificationTemplateCatalogue entry
/// since NOT-03 with no live call site. This wires the first one.
///
/// Recipient rule: the new employee's manager (ManagerId is already carried on the integration event
/// — no extra lookup needed) is notified, since the manager is the person most likely to need to
/// know a new report has joined. If there is no manager, this falls back to the company's HR
/// administrators (deterministic: every active HR administrator, mirroring
/// HR.Modules.Support.UpdateSupportRequestStatus's "notify every HR admin" fan-out rather than
/// picking just one, since onboarding awareness benefits everyone in that role, not a single
/// assignee). If neither a manager nor any HR administrator can be resolved, this is logged as a
/// warning and no notification is written (missing recipient information is handled and observable,
/// per NOT-07's acceptance criteria).
///
/// Imported employees (IsImported: true, published by DataImport's bulk-import confirmation) are
/// exempt, mirroring Probation's own EmployeeCreatedHandler decision — a bulk historical import is
/// not a "new hire" event worth notifying anyone about.
///
/// Idempotent: EmployeeCreatedIntegrationEvent delivery may repeat, so this checks
/// INotificationWriter.ExistsAsync per recipient, keyed on (recipient, employeeId, EmployeeCreated),
/// before writing.
/// </summary>
internal sealed class NotifyOnEmployeeCreatedHandler(
    INotificationWriter notificationWriter,
    IEmployeeNameReader employeeNameReader,
    IPositionProfileReader positionProfileReader,
    IHrAdministratorDirectory hrAdministratorDirectory,
    IClock clock,
    ILogger<NotifyOnEmployeeCreatedHandler> logger)
    : IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>
{
    public async Task HandleAsync(EmployeeCreatedIntegrationEvent e, CancellationToken cancellationToken)
    {
        if (e.IsImported)
            return;

        IReadOnlyList<Guid> recipientIds = e.ManagerId is { } managerId
            ? [managerId]
            : await hrAdministratorDirectory.GetHrAdministratorEmployeeIdsAsync(e.CompanyId, cancellationToken);

        if (recipientIds.Count == 0)
        {
            logger.LogWarning(
                "Skipping EmployeeCreated notification for employee {EmployeeId} in company {CompanyId}: no manager and no HR administrator could be resolved to notify.",
                e.EmployeeId, e.CompanyId);
            return;
        }

        var names = await employeeNameReader.GetNamesAsync(e.CompanyId, [e.EmployeeId], cancellationToken);
        var employeeName = names.GetValueOrDefault(e.EmployeeId, "A new employee");

        var tokens = new Dictionary<string, string> { ["EmployeeName"] = employeeName };

        if (e.PositionProfileId is { } positionProfileId)
        {
            var summary = await positionProfileReader.GetSummaryAsync(e.CompanyId, positionProfileId, cancellationToken);
            if (summary is not null)
            {
                tokens["JobTitle"] = summary.Title;
                if (summary.DepartmentName is not null)
                    tokens["Department"] = summary.DepartmentName;
            }
        }

        foreach (var recipientId in recipientIds)
        {
            var alreadySent = await notificationWriter.ExistsAsync(
                recipientId, e.EmployeeId, NotificationType.EmployeeCreated, cancellationToken);
            if (alreadySent)
                continue;

            var writeResult = await notificationWriter.WriteTemplatedAsync(
                Guid.NewGuid(), e.CompanyId, recipientId,
                NotificationType.EmployeeCreated,
                tokens,
                e.EmployeeId,
                NotificationPriority.Normal,
                clock.UtcNowOffset(),
                cancellationToken);

            if (writeResult.IsFailure)
            {
                logger.LogWarning(
                    "Failed to write EmployeeCreated notification for employee {EmployeeId} to recipient {RecipientId}: {Error}",
                    e.EmployeeId, recipientId, writeResult.Error.Message);
            }
        }
    }
}
