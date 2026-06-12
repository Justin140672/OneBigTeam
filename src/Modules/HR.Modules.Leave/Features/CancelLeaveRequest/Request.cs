namespace HR.Modules.Leave.Features.CancelLeaveRequest;

internal sealed record CancelLeaveRequestRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid LeaveRequestId { get; init; }
}
