namespace HR.Modules.Companies.Domain;

internal sealed class CompanySettings
{
    private CompanySettings() { }

    public Guid CompanyId { get; private set; }
    public string TimeZone { get; private set; } = string.Empty;
    public string Locale { get; private set; } = string.Empty;
    public string WorkingWeek { get; private set; } = string.Empty;
    public DateOnly LeaveYearStart { get; private set; }
    public decimal DefaultHolidayAllowance { get; private set; }
    public int ProbationMonths { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static CompanySettings CreateDefault(Guid companyId, DateTimeOffset now)
    {
        return new CompanySettings
        {
            CompanyId = companyId,
            TimeZone = "UTC",
            Locale = "en-GB",
            WorkingWeek = "Monday-Friday",
            LeaveYearStart = new DateOnly(2000, 1, 1),
            DefaultHolidayAllowance = 25,
            ProbationMonths = 6,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
