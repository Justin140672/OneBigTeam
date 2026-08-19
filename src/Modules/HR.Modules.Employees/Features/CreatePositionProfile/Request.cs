using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.CreatePositionProfile;

internal sealed record CreatePositionProfileRequest
{
    public Guid CompanyId { get; init; }
    public Guid DepartmentId { get; init; }
    public Guid LocationId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int? ProbationMonthsOverride { get; init; }
    public WorkingDays? WorkingDaysOverride { get; init; }
    public decimal? HoursPerDayOverride { get; init; }
    public NoticePeriodUnit? NoticePeriodUnitOverride { get; init; }
    public int? NoticePeriodLengthOverride { get; init; }
    public decimal? SalaryMin { get; init; }
    public decimal? SalaryMax { get; init; }
    public SalaryType? SalaryType { get; init; }
    public Guid DefaultLeavePolicyId { get; init; }
    public Guid? OnboardingTemplateId { get; init; }
}
