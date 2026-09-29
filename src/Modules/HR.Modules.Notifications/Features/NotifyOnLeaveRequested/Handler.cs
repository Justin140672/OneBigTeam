using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Notifications.Features.NotifyOnLeaveRequested;

internal sealed class NotifyOnLeaveRequestedHandler(
    INotificationWriter notificationWriter,
    IManagerReader managerReader,
    IEmployeeNameReader employeeNameReader,
    ILogger<NotifyOnLeaveRequestedHandler> logger)
    : IIntegrationEventHandler<LeaveRequestedIntegrationEvent>
{
    public async Task HandleAsync(LeaveRequestedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var managerId = await managerReader.GetManagerIdAsync(e.CompanyId, e.EmployeeId, cancellationToken);

        if (managerId is null)
        {
            logger.LogWarning(
                "Skipping LeaveRequested notification for leave request {LeaveRequestId}: employee {EmployeeId} in company {CompanyId} has no manager to notify.",
                e.LeaveRequestId, e.EmployeeId, e.CompanyId);
            return;
        }

        var alreadySent = await notificationWriter.ExistsAsync(
            managerId.Value, e.LeaveRequestId, NotificationType.LeaveRequested, cancellationToken);
        if (alreadySent)
            return;

        var names = await employeeNameReader.GetNamesAsync(e.CompanyId, [e.EmployeeId], cancellationToken);
        var requesterName = names.GetValueOrDefault(e.EmployeeId, "An employee");

        var writeResult = await notificationWriter.WriteTemplatedAsync(
            Guid.NewGuid(), e.CompanyId, managerId.Value,
            NotificationType.LeaveRequested,
            new Dictionary<string, string>
            {
                ["RequesterName"] = requesterName,
                ["StartDate"] = e.StartDate.ToString("d MMM yyyy"),
                ["EndDate"] = e.EndDate.ToString("d MMM yyyy"),
            },
            e.LeaveRequestId,
            NotificationPriority.Normal,
            e.OccurredAt,
            cancellationToken);

        if (writeResult.IsFailure)
        {
            logger.LogWarning(
                "Failed to write LeaveRequested notification for leave request {LeaveRequestId}: {Error}",
                e.LeaveRequestId, writeResult.Error.Message);
        }
    }
}
