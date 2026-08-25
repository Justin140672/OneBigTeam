namespace HR.Modules.Tasks.Contracts;

// OFF-06: reassigns every open (not Completed/Cancelled) TaskItem currently assigned to
// fromEmployeeId to toEmployeeId, regardless of Source/ActionType. This is the general
// catch-all used when an employee who is an assignee/approver across many task types departs
// (e.g. a manager with pending approvals/reviews of several kinds) — complementing the
// source-entity-scoped ITaskCanceller/ITaskRescheduler bulk operations, which require the caller
// to already know the set of source entity ids. Callers that own a more specific concept of
// "reassignment" for their own source entities (e.g. Probation's ManagerChangedHandler, which
// cancels/recreates ManagerCheckIn tasks so history and due dates stay correct) should continue
// to use that instead — this contract is for tasks with no such bespoke owner.
public interface ITaskReassigner
{
    /// <summary>
    /// Reassigns every open task assigned to <paramref name="fromEmployeeId"/> to
    /// <paramref name="toEmployeeId"/>. If <paramref name="toEmployeeId"/> is null, matching
    /// tasks are unassigned (AssignedEmployeeId/AssignedUserId cleared) rather than left pointing
    /// at someone no longer able to action them — callers are responsible for separately
    /// escalating unassigned tasks (e.g. to an HR exception queue).
    /// Idempotent: once a task's AssignedEmployeeId no longer equals fromEmployeeId, a repeat
    /// call with the same arguments no longer matches it and leaves it untouched.
    /// Returns the number of tasks actually reassigned by this call.
    /// </summary>
    Task<int> ReassignAllByAssigneeAsync(
        Guid companyId,
        Guid fromEmployeeId,
        Guid? toEmployeeId,
        CancellationToken cancellationToken);
}
