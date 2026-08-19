using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.SetEmployeeWorkingPattern;

internal sealed record SetEmployeeWorkingPatternRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public WorkingDays? WorkingDaysOverride { get; init; }
    public decimal? HoursPerDayOverride { get; init; }
}
