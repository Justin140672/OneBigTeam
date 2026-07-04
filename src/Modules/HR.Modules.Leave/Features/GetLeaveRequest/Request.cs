namespace HR.Modules.Leave.Features.GetLeaveRequest;

internal sealed record GetLeaveRequestRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid Id { get; init; }
}
