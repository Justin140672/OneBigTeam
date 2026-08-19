using HR.Modules.Tasks.Contracts;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Features.SicknessEvidenceRequested;

/// <summary>
/// Creates an unassigned (HR inbox) task when a sickness record exceeds the fit note threshold.
/// Runs alongside <see cref="SicknessEvidenceRequestedHandler"/>, which assigns the employee's
/// "Upload fit note" task — this handler notifies HR separately since there is no existing
/// mechanism to enumerate individual HR administrators for direct per-admin notification.
/// </summary>
internal sealed class NotifyHrOfFitNoteThresholdHandler(
    ITaskCreator taskCreator,
    IEmployeeNameReader employeeNameReader) : IIntegrationEventHandler<SicknessEvidenceRequestedIntegrationEvent>
{
    private static readonly Guid SystemUserId = Guid.Empty;

    public async Task HandleAsync(SicknessEvidenceRequestedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var names = await employeeNameReader.GetNamesAsync(e.CompanyId, [e.EmployeeId], cancellationToken);
        var employeeName = names.GetValueOrDefault(e.EmployeeId, "Unknown Employee");

        await taskCreator.CreateAsync(
            e.CompanyId,
            createdBy:          SystemUserId,
            title:              $"Fit note required — {employeeName}",
            description:        $"{employeeName}'s sickness absence has exceeded the fit note threshold. A fit note has been requested from the employee.",
            priority:           TaskPriority.Medium,
            source:             TaskSource.Sickness,
            actionType:         TaskActionType.Complete,
            dueDate:            e.DueDate,
            assignedEmployeeId: null,
            assignedUserId:     null,
            sourceEntityId:     e.EvidenceRequestId,
            cancellationToken);
    }
}
