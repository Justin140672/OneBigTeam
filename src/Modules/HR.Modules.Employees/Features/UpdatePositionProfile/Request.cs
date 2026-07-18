using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.UpdatePositionProfile;

internal sealed record UpdatePositionProfileRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public Guid DepartmentId { get; init; }
    public Guid LocationId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int? ProbationMonthsOverride { get; init; }
    public WorkingDays? WorkingDaysOverride { get; init; }
    public decimal? HoursPerDayOverride { get; init; }
    public decimal? SalaryMin { get; init; }
    public decimal? SalaryMax { get; init; }
    public SalaryType? SalaryType { get; init; }
    public Guid DefaultLeavePolicyId { get; init; }
    public Guid? OnboardingTemplateId { get; init; }
}
