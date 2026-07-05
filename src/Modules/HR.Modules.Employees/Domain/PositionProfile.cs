using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Domain;

internal sealed class PositionProfile
{
    private readonly List<PositionProfileRequiredDocument> _requiredDocuments = [];

    private PositionProfile() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? DepartmentId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsManagerial { get; private set; }
    public int? ProbationMonthsOverride { get; private set; }
    public WorkingDays? WorkingDaysOverride { get; private set; }
    public decimal? HoursPerDayOverride { get; private set; }
    public decimal? SalaryMin { get; private set; }
    public decimal? SalaryMax { get; private set; }
    public SalaryType? SalaryType { get; private set; }
    public Guid? DefaultLeavePolicyId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<PositionProfileRequiredDocument> RequiredDocuments => _requiredDocuments.AsReadOnly();

    public static PositionProfile Create(
        Guid id,
        Guid companyId,
        Guid? departmentId,
        string title,
        string? description,
        bool isManagerial,
        int? probationMonthsOverride,
        WorkingDays? workingDaysOverride,
        decimal? hoursPerDayOverride,
        decimal? salaryMin,
        decimal? salaryMax,
        SalaryType? salaryType,
        Guid? defaultLeavePolicyId,
        DateTimeOffset now)
    {
        return new PositionProfile
        {
            Id = id,
            CompanyId = companyId,
            DepartmentId = departmentId,
            Title = title,
            Description = description,
            IsManagerial = isManagerial,
            ProbationMonthsOverride = probationMonthsOverride,
            WorkingDaysOverride = workingDaysOverride,
            HoursPerDayOverride = hoursPerDayOverride,
            SalaryMin = salaryMin,
            SalaryMax = salaryMax,
            SalaryType = salaryType,
            DefaultLeavePolicyId = defaultLeavePolicyId,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(
        Guid? departmentId,
        string title,
        string? description,
        bool isManagerial,
        int? probationMonthsOverride,
        WorkingDays? workingDaysOverride,
        decimal? hoursPerDayOverride,
        decimal? salaryMin,
        decimal? salaryMax,
        SalaryType? salaryType,
        Guid? defaultLeavePolicyId,
        DateTimeOffset now)
    {
        DepartmentId = departmentId;
        Title = title;
        Description = description;
        IsManagerial = isManagerial;
        ProbationMonthsOverride = probationMonthsOverride;
        WorkingDaysOverride = workingDaysOverride;
        HoursPerDayOverride = hoursPerDayOverride;
        SalaryMin = salaryMin;
        SalaryMax = salaryMax;
        SalaryType = salaryType;
        DefaultLeavePolicyId = defaultLeavePolicyId;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }
}
