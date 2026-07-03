using HR.Modules.Companies.Features.UpdateCompanySettings;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Companies.Tests;

public class UpdateCompanySettingsValidatorTests
{
	private static UpdateCompanySettingsRequest ValidRequest() => new()
	{
		Id = Guid.NewGuid(),
		TimeZone = "Europe/London",
		Locale = "en-GB",
		WorkingDays = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
		              WorkingDays.Thursday | WorkingDays.Friday,
		HoursPerDay = 7.5m,
		LeaveYearStartMonth = 4,
		DefaultHolidayAllowance = 28,
		ProbationMonths = 3,
	};

	[Fact]
	public void Validate_Passes_For_Valid_Request()
	{
		var validator = new UpdateCompanySettingsValidator();
		Assert.True(validator.Validate(ValidRequest()).IsValid);
	}

	[Fact]
	public void Validate_Fails_When_WorkingDays_Is_None()
	{
		var validator = new UpdateCompanySettingsValidator();
		var result = validator.Validate(ValidRequest() with { WorkingDays = WorkingDays.None });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCompanySettingsRequest.WorkingDays));
	}

	[Fact]
	public void Validate_Fails_When_HoursPerDay_Is_Zero()
	{
		var validator = new UpdateCompanySettingsValidator();
		var result = validator.Validate(ValidRequest() with { HoursPerDay = 0 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCompanySettingsRequest.HoursPerDay));
	}

	[Fact]
	public void Validate_Fails_When_HoursPerDay_Exceeds_24()
	{
		var validator = new UpdateCompanySettingsValidator();
		var result = validator.Validate(ValidRequest() with { HoursPerDay = 24.5m });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCompanySettingsRequest.HoursPerDay));
	}

	[Fact]
	public void Validate_Fails_When_Leave_Year_Start_Month_Is_Invalid()
	{
		var validator = new UpdateCompanySettingsValidator();
		var result = validator.Validate(ValidRequest() with { LeaveYearStartMonth = 13 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateCompanySettingsRequest.LeaveYearStartMonth));
	}

	[Fact]
	public void Validate_Fails_When_DefaultHolidayAllowance_Is_Invalid()
	{
		var validator = new UpdateCompanySettingsValidator();
		var result = validator.Validate(ValidRequest() with { DefaultHolidayAllowance = 0 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateCompanySettingsRequest.DefaultHolidayAllowance));
	}
}
