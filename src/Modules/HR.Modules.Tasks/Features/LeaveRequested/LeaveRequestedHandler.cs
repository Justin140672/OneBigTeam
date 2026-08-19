using HR.Modules.Tasks.Contracts;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Features.LeaveRequested;

internal sealed class LeaveRequestedHandler(
    ITaskCreator taskCreator,
    IEmployeeNameReader employeeNameReader,
    IManagerReader managerReader) : IIntegrationEventHandler<LeaveRequestedIntegrationEvent>
{
    public async Task HandleAsync(LeaveRequestedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var names = await employeeNameReader.GetNamesAsync(e.CompanyId, [e.EmployeeId], cancellationToken);
        var employeeName = names.GetValueOrDefault(e.EmployeeId, "Unknown Employee");

        var managerId = await managerReader.GetManagerIdAsync(e.CompanyId, e.EmployeeId, cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dueDate = e.StartDate > today ? e.StartDate : today;

        var dayLabel = e.TotalDays == 1 ? "day" : "days";

        await taskCreator.CreateAsync(
            e.CompanyId,
            e.EmployeeId,
            $"Review leave request — {employeeName}",
            $"{e.StartDate:d MMM yyyy} to {e.EndDate:d MMM yyyy} · {e.TotalDays} {dayLabel}. Review and approve or reject this request before the leave begins.",
            TaskPriority.Medium,
            TaskSource.Leave,
            TaskActionType.Approve,
            dueDate,
            assignedEmployeeId: managerId,
            assignedUserId: managerId,
            sourceEntityId: e.LeaveRequestId,
            cancellationToken);
    }
}
