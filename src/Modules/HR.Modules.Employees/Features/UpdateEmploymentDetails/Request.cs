using HR.Modules.Employees.Domain;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.UpdateEmploymentDetails;

internal sealed record UpdateEmploymentDetailsRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public string? EmployeeNumber { get; init; }
    public Guid? EmploymentTypeId { get; init; }
    public EmploymentStatus Status { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? PositionProfileId { get; init; }
    public Guid? ManagerId { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly? ContinuousServiceDate { get; init; }
    public DateOnly? ProbationEndDate { get; init; }
    public DateOnly? LeavingDate { get; init; }
    public WorkingDays? WorkingDaysOverride { get; init; }
    public decimal? HoursPerDayOverride { get; init; }
    public string? Notes { get; init; }
}
