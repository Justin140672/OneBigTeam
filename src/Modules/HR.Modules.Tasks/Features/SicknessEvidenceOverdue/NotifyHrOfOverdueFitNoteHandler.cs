using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Features.SicknessEvidenceOverdue;

/// <summary>
/// Creates an unassigned (HR inbox) task when a fit note evidence request becomes overdue.
/// Mirrors NotifyHrOfFitNoteThresholdHandler's approach (there is no existing mechanism to
/// enumerate individual HR administrators for direct per-admin notification), but reacts to
/// the request actually going overdue rather than the initial threshold-exceeded trigger.
/// Uses TaskActionType.Complete, not Review — Source=Sickness/ActionType=Review is reserved
/// for return-to-work review tasks in TaskView.razor's dispatch, and this task's
/// SourceEntityId is a SicknessEvidenceRequest.Id, not a ReturnToWorkReview.Id.
/// </summary>
internal sealed class NotifyHrOfOverdueFitNoteHandler(
    ITaskCreator taskCreator,
    IEmployeeNameReader employeeNameReader) : IIntegrationEventHandler<SicknessEvidenceOverdueIntegrationEvent>
{
    private static readonly Guid SystemUserId = Guid.Empty;

    public async Task HandleAsync(SicknessEvidenceOverdueIntegrationEvent e, CancellationToken cancellationToken)
    {
        var names = await employeeNameReader.GetNamesAsync(e.CompanyId, [e.EmployeeId], cancellationToken);
        var employeeName = names.GetValueOrDefault(e.EmployeeId, "Unknown Employee");

        await taskCreator.CreateAsync(
            e.CompanyId,
            createdBy:          SystemUserId,
            title:              $"Fit note overdue — {employeeName}",
            description:        $"{employeeName}'s fit note evidence request is now overdue. Follow up with the employee.",
            priority:           TaskPriority.High,
            source:             TaskSource.Sickness,
            actionType:         TaskActionType.Complete,
            dueDate:            e.DueDate,
            assignedEmployeeId: null,
            assignedUserId:     null,
            sourceEntityId:     e.EvidenceRequestId,
            cancellationToken);
    }
}
