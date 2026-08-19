using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;

namespace HR.Modules.Employees.Domain;

internal sealed class PositionProfile
{
    private readonly List<PositionProfileRequiredDocument> _requiredDocuments = [];
    private readonly List<PositionProfileRequiredAsset> _requiredAssets = [];

    private PositionProfile() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid DepartmentId { get; private set; }
    public Guid LocationId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public int? ProbationMonthsOverride { get; private set; }
    public WorkingDays? WorkingDaysOverride { get; private set; }
    public decimal? HoursPerDayOverride { get; private set; }
    public NoticePeriodUnit? NoticePeriodUnitOverride { get; private set; }
    public int? NoticePeriodLengthOverride { get; private set; }
    public decimal? SalaryMin { get; private set; }
    public decimal? SalaryMax { get; private set; }
    public SalaryType? SalaryType { get; private set; }
    public Guid DefaultLeavePolicyId { get; private set; }
    public Guid? OnboardingTemplateId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<PositionProfileRequiredDocument> RequiredDocuments => _requiredDocuments.AsReadOnly();
    public IReadOnlyList<PositionProfileRequiredAsset> RequiredAssets => _requiredAssets.AsReadOnly();

    public static PositionProfile Create(
        Guid id,
        Guid companyId,
        Guid departmentId,
        Guid locationId,
        string title,
        string? description,
        int? probationMonthsOverride,
        WorkingDays? workingDaysOverride,
        decimal? hoursPerDayOverride,
        decimal? salaryMin,
        decimal? salaryMax,
        SalaryType? salaryType,
        Guid defaultLeavePolicyId,
        DateTimeOffset now,
        Guid? onboardingTemplateId = null,
        NoticePeriodUnit? noticePeriodUnitOverride = null,
        int? noticePeriodLengthOverride = null)
    {
        return new PositionProfile
        {
            Id = id,
            CompanyId = companyId,
            DepartmentId = departmentId,
            LocationId = locationId,
            Title = title,
            Description = description,
            ProbationMonthsOverride = probationMonthsOverride,
            WorkingDaysOverride = workingDaysOverride,
            HoursPerDayOverride = hoursPerDayOverride,
            NoticePeriodUnitOverride = noticePeriodUnitOverride,
            NoticePeriodLengthOverride = noticePeriodLengthOverride,
            SalaryMin = salaryMin,
            SalaryMax = salaryMax,
            SalaryType = salaryType,
            DefaultLeavePolicyId = defaultLeavePolicyId,
            OnboardingTemplateId = onboardingTemplateId,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(
        Guid departmentId,
        Guid locationId,
        string title,
        string? description,
        int? probationMonthsOverride,
        WorkingDays? workingDaysOverride,
        decimal? hoursPerDayOverride,
        decimal? salaryMin,
        decimal? salaryMax,
        SalaryType? salaryType,
        Guid defaultLeavePolicyId,
        DateTimeOffset now,
        Guid? onboardingTemplateId = null,
        NoticePeriodUnit? noticePeriodUnitOverride = null,
        int? noticePeriodLengthOverride = null)
    {
        DepartmentId = departmentId;
        LocationId = locationId;
        Title = title;
        Description = description;
        ProbationMonthsOverride = probationMonthsOverride;
        WorkingDaysOverride = workingDaysOverride;
        HoursPerDayOverride = hoursPerDayOverride;
        NoticePeriodUnitOverride = noticePeriodUnitOverride;
        NoticePeriodLengthOverride = noticePeriodLengthOverride;
        SalaryMin = salaryMin;
        SalaryMax = salaryMax;
        SalaryType = salaryType;
        DefaultLeavePolicyId = defaultLeavePolicyId;
        OnboardingTemplateId = onboardingTemplateId;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }
}
