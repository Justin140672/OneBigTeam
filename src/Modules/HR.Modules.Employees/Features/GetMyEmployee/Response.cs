using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.GetMyEmployee;

internal sealed record GetMyEmployeeResponse(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    string? JobTitle,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    string? ProfileImageUrl,
    bool RequiresInitialSetup);
