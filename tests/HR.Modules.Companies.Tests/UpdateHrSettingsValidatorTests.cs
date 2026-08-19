using HR.Modules.Companies.Features.UpdateHrSettings;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Companies.Tests;

public class UpdateHrSettingsValidatorTests
{
	private static UpdateHrSettingsRequest ValidRequest() => new()
	{
		Id = Guid.NewGuid(),
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
		var validator = new UpdateHrSettingsValidator();
		Assert.True(validator.Validate(ValidRequest()).IsValid);
	}

	[Fact]
	public void Validate_Fails_When_WorkingDays_Is_None()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { WorkingDays = WorkingDays.None });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateHrSettingsRequest.WorkingDays));
	}

	[Fact]
	public void Validate_Fails_When_HoursPerDay_Is_Zero()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { HoursPerDay = 0 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateHrSettingsRequest.HoursPerDay));
	}

	[Fact]
	public void Validate_Fails_When_HoursPerDay_Exceeds_24()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { HoursPerDay = 24.5m });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateHrSettingsRequest.HoursPerDay));
	}

	[Fact]
	public void Validate_Fails_When_Leave_Year_Start_Month_Is_Invalid()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { LeaveYearStartMonth = 13 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.LeaveYearStartMonth));
	}

	[Fact]
	public void Validate_Fails_When_DefaultHolidayAllowance_Is_Invalid()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { DefaultHolidayAllowance = 0 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.DefaultHolidayAllowance));
	}

	[Fact]
	public void Validate_Passes_When_DefaultAcknowledgementStatement_Is_Blank()
	{
		// The domain-level fallback to the hardcoded default happens in CompanySettings.UpdateHrPolicy,
		// not here — a blank string must pass the validator.
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { DefaultAcknowledgementStatement = string.Empty });
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Passes_When_DefaultAcknowledgementStatement_Is_Whitespace()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { DefaultAcknowledgementStatement = "   " });
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Passes_When_DefaultAcknowledgementStatement_At_MaximumLength()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { DefaultAcknowledgementStatement = new string('a', 2000) });
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Fails_When_DefaultAcknowledgementStatement_Exceeds_MaximumLength()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { DefaultAcknowledgementStatement = new string('a', 2001) });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.DefaultAcknowledgementStatement));
	}

	[Fact]
	public void Validate_Passes_For_Valid_NoticePeriodLength()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { NoticePeriodUnit = NoticePeriodUnit.Weeks, NoticePeriodLength = 2 });
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Fails_When_NoticePeriodLength_Is_Zero()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { NoticePeriodLength = 0 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.NoticePeriodLength));
	}

	[Fact]
	public void Validate_Fails_When_NoticePeriodLength_Is_Negative()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { NoticePeriodLength = -1 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.NoticePeriodLength));
	}

	[Fact]
	public void Validate_Passes_For_Valid_NextEmployeeNumber()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { NextEmployeeNumber = 1 });
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Fails_When_NextEmployeeNumber_Is_Zero()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { NextEmployeeNumber = 0 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.NextEmployeeNumber));
	}

	[Fact]
	public void Validate_Fails_When_NextEmployeeNumber_Is_Negative()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { NextEmployeeNumber = -1 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.NextEmployeeNumber));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(10)]
	public void Validate_Passes_For_EmployeeNumberMinimumLength_At_Boundaries(int minimumLength)
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { EmployeeNumberMinimumLength = minimumLength });
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Fails_When_EmployeeNumberMinimumLength_Is_Zero()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { EmployeeNumberMinimumLength = 0 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.EmployeeNumberMinimumLength));
	}

	[Fact]
	public void Validate_Fails_When_EmployeeNumberMinimumLength_Exceeds_Ten()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { EmployeeNumberMinimumLength = 11 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.EmployeeNumberMinimumLength));
	}

	[Fact]
	public void Validate_Passes_When_EmployeeNumberPrefix_Is_Null()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { EmployeeNumberPrefix = null });
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Passes_When_EmployeeNumberPrefix_At_MaximumLength()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { EmployeeNumberPrefix = new string('A', 20) });
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Fails_When_EmployeeNumberPrefix_Exceeds_MaximumLength()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { EmployeeNumberPrefix = new string('A', 21) });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.EmployeeNumberPrefix));
	}
}
