namespace HR.Modules.Leave.Features.ApproveLeaveRequest;

internal sealed record ApproveLeaveRequestRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid LeaveRequestId { get; init; }
    public Guid ReviewedByEmployeeId { get; init; }
}
