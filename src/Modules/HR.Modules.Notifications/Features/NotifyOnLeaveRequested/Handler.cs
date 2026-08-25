using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Notifications.Features.NotifyOnLeaveRequested;

/// <summary>
/// NOT-07: raises a LeaveRequested notification for the requesting employee's manager when a leave
/// request is submitted and requires approval. Before this handler, LeaveRequestedIntegrationEvent
/// only drove HR.Modules.Tasks' approval task (see LeaveRequestedHandler there) — the manager was
/// never separately notified in-app/by-email, only handed a task. This closes that gap using the
/// same event, per the "one business event can create the required in-app and email deliveries"
/// acceptance criterion, without duplicating SubmitLeaveRequestHandler's direct-call plumbing.
///
/// Recipient rule: the requesting employee's current manager (resolved via IManagerReader, the same
/// contract Tasks' own LeaveRequestedHandler already uses for its approval task assignee). If the
/// employee has no manager, there is no well-defined single approver to notify — this is logged as a
/// warning and no notification is written, mirroring the "missing recipient information is handled
/// and observable" acceptance criterion (an unassigned approval task, not a silently-dropped
/// notification, is the primary signal in that case).
///
/// Idempotent: LeaveRequestedIntegrationEvent delivery may repeat (04-event-architecture.md consumer
/// idempotency requirement), so this checks INotificationWriter.ExistsAsync keyed on
/// (manager, leaveRequestId, LeaveRequested) before writing, the same pattern used by
/// ProbationOutcomeNotifier/LeaveApprovalEffectsService elsewhere in this module set.
/// </summary>
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
