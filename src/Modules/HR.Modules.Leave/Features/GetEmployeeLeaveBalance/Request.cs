namespace HR.Modules.Leave.Features.GetEmployeeLeaveBalance;

internal sealed record GetEmployeeLeaveBalanceRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public int PolicyYear { get; init; }
}
