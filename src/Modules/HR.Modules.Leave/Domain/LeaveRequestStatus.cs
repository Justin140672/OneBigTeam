namespace HR.Modules.Leave.Domain;

internal enum LeaveRequestStatus
{
    // A draft is a not-yet-submitted leave request owned exclusively by the employee who
    // created it (LEAVE-07). It never touches LeaveBalance/ToilTransaction, never creates an
    // approval task, and never triggers a notification. Only Draft rows may be edited-as-draft
    // or hard-deleted; every other status is a terminal or in-flight submitted state.
    Draft,
    Pending,
    Approved,
    Rejected,
    Cancelled,
    Withdrawn
}
