using HR.Modules.Companies.Features.UpdateCompanySettings;

namespace HR.Modules.Companies.Tests;

public class UpdateCompanySettingsValidatorTests
{
	[Fact]
	public void Validate_Passes_For_Valid_Request()
	{
		var validator = new UpdateCompanySettingsValidator();

		var result = validator.Validate(new UpdateCompanySettingsRequest
		{
			Id = Guid.NewGuid(),
			TimeZone = "Europe/London",
			Locale = "en-GB",
			WorkingWeek = "Monday-Friday",
			LeaveYearStartMonth = 4,
			DefaultHolidayAllowance = 28,
			ProbationMonths = 3,
		});

		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Fails_When_Leave_Year_Start_Month_Is_Invalid()
	{
		var validator = new UpdateCompanySettingsValidator();

		var result = validator.Validate(new UpdateCompanySettingsRequest
		{
			Id = Guid.NewGuid(),
			TimeZone = "UTC",
			Locale = "en-GB",
			WorkingWeek = "Monday-Friday",
			LeaveYearStartMonth = 13,
			DefaultHolidayAllowance = 25,
			ProbationMonths = 6,
		});

		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateCompanySettingsRequest.LeaveYearStartMonth));
	}

	[Fact]
	public void Validate_Fails_When_DefaultHolidayAllowance_Is_Invalid()
	{
		var validator = new UpdateCompanySettingsValidator();

		var result = validator.Validate(new UpdateCompanySettingsRequest
		{
			Id = Guid.NewGuid(),
			TimeZone = "UTC",
			Locale = "en-GB",
			WorkingWeek = "Monday-Friday",
			LeaveYearStartMonth = 1,
			DefaultHolidayAllowance = 0,
			ProbationMonths = 6,
		});

		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateCompanySettingsRequest.DefaultHolidayAllowance));
	}
}
