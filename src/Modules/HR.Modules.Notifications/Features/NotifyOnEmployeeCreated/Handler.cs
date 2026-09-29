using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Notifications.Features.NotifyOnEmployeeCreated;

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
