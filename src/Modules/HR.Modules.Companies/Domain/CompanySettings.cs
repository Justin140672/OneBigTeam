using HR.SharedKernel;

namespace HR.Modules.Companies.Domain;

internal sealed class CompanySettings
{
    private CompanySettings() { }

    public Guid CompanyId { get; private set; }
    public string TimeZone { get; private set; } = string.Empty;
    public string Locale { get; private set; } = string.Empty;
    public WorkingDays WorkingDays { get; private set; }
    public decimal HoursPerDay { get; private set; }
    public int LeaveYearStartMonth { get; private set; }
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
            WorkingDays = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
                          WorkingDays.Thursday | WorkingDays.Friday,
            HoursPerDay = 7.5m,
            LeaveYearStartMonth = 1,
            DefaultHolidayAllowance = 25,
            ProbationMonths = 6,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(
        string timeZone,
        string locale,
        WorkingDays workingDays,
        decimal hoursPerDay,
        int leaveYearStartMonth,
        decimal defaultHolidayAllowance,
        int probationMonths,
        DateTimeOffset now)
    {
        TimeZone = timeZone;
        Locale = locale;
        WorkingDays = workingDays;
        HoursPerDay = hoursPerDay;
        LeaveYearStartMonth = leaveYearStartMonth;
        DefaultHolidayAllowance = defaultHolidayAllowance;
        ProbationMonths = probationMonths;
        UpdatedAt = now;
    }
}
