namespace HR.Modules.Employees.Domain;

internal sealed class Compensation
{
    private Compensation() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public SalaryType SalaryType { get; private set; }
    public decimal Salary { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public decimal? HoursPerWeek { get; private set; }
    public decimal? FTE { get; private set; }
    public string? Notes { get; private set; }
    public CompensationChangeReason Reason { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Compensation Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        DateOnly effectiveFrom,
        SalaryType salaryType,
        decimal salary,
        string currency,
        decimal? hoursPerWeek,
        decimal? fte,
        string? notes,
        CompensationChangeReason reason,
        Guid createdBy,
        DateTimeOffset now)
    {
        return new Compensation
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            EffectiveFrom = effectiveFrom,
            SalaryType = salaryType,
            Salary = salary,
            Currency = currency,
            HoursPerWeek = hoursPerWeek,
            FTE = fte,
            Notes = notes,
            Reason = reason,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Close(DateOnly effectiveTo, DateTimeOffset now)
    {
        EffectiveTo = effectiveTo;
        UpdatedAt = now;
    }

    public void Reopen(DateTimeOffset now)
    {
        EffectiveTo = null;
        UpdatedAt = now;
    }

    public void Update(
        SalaryType salaryType,
        decimal salary,
        string currency,
        decimal? hoursPerWeek,
        decimal? fte,
        string? notes,
        CompensationChangeReason reason,
        DateTimeOffset now)
    {
        // CreatedBy is intentionally not touched here — it records who originally created the
        // record, matching how CreatedAt behaves. Editing a future-dated record doesn't change
        // who created it.
        SalaryType = salaryType;
        Salary = salary;
        Currency = currency;
        HoursPerWeek = hoursPerWeek;
        FTE = fte;
        Notes = notes;
        Reason = reason;
        UpdatedAt = now;
    }

    // Standard 5-day working week * 52 weeks; Hourly needs HoursPerWeek to annualise and returns
    // null without it rather than guessing a working pattern.
    private const int WorkingDaysPerYear = 260;

    public decimal? CalculateAnnualisedSalary() => SalaryType switch
    {
        SalaryType.Annual => Salary,
        SalaryType.Hourly => HoursPerWeek.HasValue ? Salary * HoursPerWeek.Value * 52 : null,
        SalaryType.Daily => Salary * WorkingDaysPerYear,
        _ => null
    };
}
