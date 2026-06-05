namespace HR.Modules.Companies.Features.UpdateCompanySettings;

internal sealed record UpdateCompanySettingsRequest
{
	public Guid Id { get; init; }
	public string TimeZone { get; init; } = string.Empty;
	public string Locale { get; init; } = string.Empty;
	public string WorkingWeek { get; init; } = string.Empty;
	public int LeaveYearStartMonth { get; init; }
	public decimal DefaultHolidayAllowance { get; init; }
	public int ProbationMonths { get; init; }
}
