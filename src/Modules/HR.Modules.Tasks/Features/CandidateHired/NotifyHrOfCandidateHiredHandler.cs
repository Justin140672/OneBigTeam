using HR.Modules.Tasks.Contracts;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Features.CandidateHired;

/// <summary>
/// Creates an unassigned (HR inbox) task when a candidate is hired. Mirrors
/// NotifyHrOfFitNoteThresholdHandler/NotifyHrOfOverdueFitNoteHandler's approach — there is no
/// existing mechanism to enumerate individual HR administrators for direct per-admin
/// notification, so the task is left unassigned and surfaces in the HR Inbox
/// (see GetUnassignedTasks) for any HR administrator to pick up.
/// </summary>
internal sealed class NotifyHrOfCandidateHiredHandler(
    ITaskCreator taskCreator,
    IEmployeeNameReader employeeNameReader) : IIntegrationEventHandler<CandidateHiredIntegrationEvent>
{
    private static readonly Guid SystemUserId = Guid.Empty;

    public async Task HandleAsync(CandidateHiredIntegrationEvent e, CancellationToken cancellationToken)
    {
        var names = await employeeNameReader.GetNamesAsync(e.CompanyId, [e.EmployeeId], cancellationToken);
        var employeeName = names.GetValueOrDefault(e.EmployeeId, "Unknown Employee");

        await taskCreator.CreateAsync(
            e.CompanyId,
            createdBy:          SystemUserId,
            title:              $"Candidate hired — {employeeName}",
            description:        $"{employeeName} has been hired and provisioned as an employee. Complete any outstanding onboarding and recruitment close-out steps.",
            priority:           TaskPriority.Medium,
            source:             TaskSource.Recruitment,
            actionType:         TaskActionType.Review,
            dueDate:            null,
            assignedEmployeeId: null,
            assignedUserId:     null,
            sourceEntityId:     e.ApplicationId,
            cancellationToken);
    }
}
