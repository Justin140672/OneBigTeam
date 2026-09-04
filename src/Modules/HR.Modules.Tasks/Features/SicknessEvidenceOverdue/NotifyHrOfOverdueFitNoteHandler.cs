using HR.Modules.Tasks.Contracts;
using HR.Modules.Employees.Contracts;
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
///
/// <para>
/// OBT-REM-10: the SicknessEvidenceReminderJob may publish this event more than once for the same
/// evidence request — e.g. after a Hangfire retry, or because the event's own progress marker was
/// committed but the job process crashed before that commit was durable. The evidence request id
/// (SourceEntityId) is a stable, deterministic identity for the underlying overdue occurrence, so
/// this handler checks for an existing open task against that id before creating another rather
/// than relying on the publisher to guarantee exactly-once delivery.
/// </para>
///
/// <para>
/// OBT-REM-13: the read-before-create check above is a check-then-act race — two concurrent
/// deliveries of the same event (e.g. overlapping Hangfire executions, or a retry racing the
/// original attempt) can both observe "no open task" and both proceed to create one. Correctness
/// against that race comes from <see cref="ITaskCreator.CreateAsync"/>'s idempotencyKey parameter,
/// backed by a database unique constraint (see TaskItemConfiguration) — not from the read check,
/// which remains purely an optimisation to skip unnecessary work in the common case.
/// </para>
/// </summary>
internal sealed class NotifyHrOfOverdueFitNoteHandler(
    ITaskCreator taskCreator,
    IOpenTaskBySourceEntityReader openTaskReader,
    IEmployeeNameReader employeeNameReader) : IIntegrationEventHandler<SicknessEvidenceOverdueIntegrationEvent>
{
    private static readonly Guid SystemUserId = Guid.Empty;

    public async Task HandleAsync(SicknessEvidenceOverdueIntegrationEvent e, CancellationToken cancellationToken)
    {
        var openTasks = await openTaskReader.GetOpenTaskIdsAsync(
            e.CompanyId, [e.EvidenceRequestId], cancellationToken, TaskActionType.Complete);
        if (openTasks.ContainsKey(e.EvidenceRequestId))
            return;

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
            cancellationToken,
            idempotencyKey:     $"SicknessEvidenceOverdue:{e.EvidenceRequestId}");
    }
}
