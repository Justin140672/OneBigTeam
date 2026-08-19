using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.SetEmployeeWorkingPattern;

internal sealed record SetEmployeeWorkingPatternResponse(
    Guid EmployeeId,
    Guid CompanyId,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    DateTimeOffset UpdatedAt);
