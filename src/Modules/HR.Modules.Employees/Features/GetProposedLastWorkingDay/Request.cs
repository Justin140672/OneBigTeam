namespace HR.Modules.Employees.Features.GetProposedLastWorkingDay;

internal sealed record GetProposedLastWorkingDayRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public DateOnly LeavingDate { get; init; }
}
