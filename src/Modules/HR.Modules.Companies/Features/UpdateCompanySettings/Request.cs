using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Features.UpdateCompanySettings;

internal sealed record UpdateCompanySettingsRequest
{
    public Guid Id { get; init; }
    public string TimeZone { get; init; } = string.Empty;
    public string Locale { get; init; } = string.Empty;
    public WorkingDays WorkingWeek { get; init; }
    public int LeaveYearStartMonth { get; init; }
    public decimal DefaultHolidayAllowance { get; init; }
    public int ProbationMonths { get; init; }
}
