namespace HR.Modules.Employees.Domain;

internal sealed class Compensation
{
    private Compensation() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public SalaryType SalaryType { get; private set; }
    public decimal Salary { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public decimal? HoursPerWeek { get; private set; }
    public decimal? FTE { get; private set; }
    public string? Notes { get; private set; }
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
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
