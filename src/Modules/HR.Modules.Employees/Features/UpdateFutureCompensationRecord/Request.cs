using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.UpdateFutureCompensationRecord;

internal sealed record UpdateFutureCompensationRecordRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid Id { get; init; }
    public SalaryType SalaryType { get; init; }
    public decimal Salary { get; init; }
    public string Currency { get; init; } = string.Empty;
    public decimal? HoursPerWeek { get; init; }
    public decimal? FTE { get; init; }
    public string? Notes { get; init; }
    public CompensationChangeReason Reason { get; init; }
}
