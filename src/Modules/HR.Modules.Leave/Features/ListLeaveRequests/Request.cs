namespace HR.Modules.Leave.Features.ListLeaveRequests;

internal sealed class ListLeaveRequestsRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
}
