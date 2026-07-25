using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.UpdatePositionProfile;

internal sealed record UpdatePositionProfileResponse(
    Guid Id,
    Guid CompanyId,
    Guid DepartmentId,
    Guid LocationId,
    string Title,
    string? Description,
    int? ProbationMonthsOverride,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    NoticePeriodUnit? NoticePeriodUnitOverride,
    int? NoticePeriodLengthOverride,
    decimal? SalaryMin,
    decimal? SalaryMax,
    SalaryType? SalaryType,
    Guid DefaultLeavePolicyId,
    Guid? OnboardingTemplateId,
    bool IsActive,
    DateTimeOffset UpdatedAt);
