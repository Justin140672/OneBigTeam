namespace HR.Modules.Leave.Domain;

internal enum LeaveRequestStatus
{
    Draft,
    Pending,
    Approved,
    Rejected,
    Cancelled,
    Withdrawn
}
