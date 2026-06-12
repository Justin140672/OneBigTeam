namespace HR.Modules.Leave.Features.RejectLeaveRequest;

internal sealed record RejectLeaveRequestRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid LeaveRequestId { get; init; }
    public Guid ReviewedByEmployeeId { get; init; }
    public string? RejectionReason { get; init; }
}
