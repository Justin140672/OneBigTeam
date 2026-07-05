using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Features.UpdatePositionProfile;

internal sealed record UpdatePositionProfileResponse(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    string Title,
    string? Description,
    bool IsManagerial,
    int? ProbationMonthsOverride,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    decimal? SalaryMin,
    decimal? SalaryMax,
    Guid? DefaultLeavePolicyId,
    bool IsActive,
    DateTimeOffset UpdatedAt);
