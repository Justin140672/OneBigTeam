using HR.Modules.Companies.Features.UpdateHrSettings;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Companies.Tests;

public class UpdateHrSettingsValidatorTests
{
	private static UpdateHrSettingsRequest ValidRequest() => new()
	{
		CompanyId = Guid.NewGuid(),
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
	public void Validate_Passes_When_DefaultHolidayAllowance_Is_Zero()
	{
		// SET-01: zero is now an allowed value (only negative values are rejected).
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { DefaultHolidayAllowance = 0 });
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Fails_When_DefaultHolidayAllowance_Is_Negative()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { DefaultHolidayAllowance = -1 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.DefaultHolidayAllowance));
	}

	[Fact]
	public void Validate_Passes_When_DefaultHolidayAllowance_At_Upper_Boundary()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { DefaultHolidayAllowance = 365 });
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Fails_When_DefaultHolidayAllowance_Exceeds_Upper_Boundary()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { DefaultHolidayAllowance = 366 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.DefaultHolidayAllowance));
	}

	[Fact]
	public void Validate_Fails_When_ProbationMonths_Is_Zero()
	{
		// SET-01: ProbationMonths must now be strictly greater than zero.
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { ProbationMonths = 0 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.ProbationMonths));
	}

	[Fact]
	public void Validate_Fails_When_ProbationMonths_Is_Negative()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { ProbationMonths = -1 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.ProbationMonths));
	}

	[Fact]
	public void Validate_Passes_When_ProbationMonths_Is_One()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { ProbationMonths = 1 });
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Passes_When_ProbationMonths_At_Upper_Boundary()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { ProbationMonths = 24 });
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Fails_When_ProbationMonths_Exceeds_Upper_Boundary()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { ProbationMonths = 25 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.ProbationMonths));
	}

	[Fact]
	public void Validate_Fails_When_WorkingDays_Contains_A_Bit_Outside_The_Defined_Flags()
	{
		// SET-01: any bit outside the 7 defined WorkingDays flags (values 1,2,4,8,16,32,64 — i.e.
		// anything satisfying (value & ~127) != 0) must be rejected, not silently ignored.
		var validator = new UpdateHrSettingsValidator();
		var undefinedWorkingDays = (WorkingDays)128;
		var result = validator.Validate(ValidRequest() with { WorkingDays = WorkingDays.Monday | undefinedWorkingDays });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.WorkingDays));
	}

	[Fact]
	public void Validate_Passes_When_WorkingDays_Is_All_Seven_Defined_Flags()
	{
		var validator = new UpdateHrSettingsValidator();
		const WorkingDays allDefinedWorkingDays = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
			WorkingDays.Thursday | WorkingDays.Friday | WorkingDays.Saturday | WorkingDays.Sunday;
		var result = validator.Validate(ValidRequest() with { WorkingDays = allDefinedWorkingDays });
		Assert.True(result.IsValid);
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

	// SET-04: probation checkpoints.

	[Fact]
	public void Validate_Passes_When_ProbationCheckpoints_Are_Valid_And_Strictly_Increasing()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with
		{
			ProbationMonths = 6,
			ProbationCheckpointDay1 = 30,
			ProbationCheckpointDay2 = 60,
			ProbationCheckpointDay3 = 90,
		});
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Passes_When_All_ProbationCheckpoints_Are_Null()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with
		{
			ProbationCheckpointDay1 = null,
			ProbationCheckpointDay2 = null,
			ProbationCheckpointDay3 = null,
		});
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Passes_When_Only_A_Later_ProbationCheckpoint_Is_Configured()
	{
		// Day1 = null, Day2 = 10, Day3 = 20 — nulls are simply skipped, not required to be trailing.
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with
		{
			ProbationMonths = 6,
			ProbationCheckpointDay1 = null,
			ProbationCheckpointDay2 = 10,
			ProbationCheckpointDay3 = 20,
		});
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Fails_When_ProbationCheckpointDay1_Is_Zero()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { ProbationCheckpointDay1 = 0 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.ProbationCheckpointDay1));
	}

	[Fact]
	public void Validate_Fails_When_ProbationCheckpointDay1_Is_Negative()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { ProbationCheckpointDay1 = -5 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.ProbationCheckpointDay1));
	}

	[Fact]
	public void Validate_Fails_When_ProbationCheckpoints_Contain_A_Duplicate_Value()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with
		{
			ProbationMonths = 6,
			ProbationCheckpointDay1 = 30,
			ProbationCheckpointDay2 = 30,
			ProbationCheckpointDay3 = null,
		});
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == "ProbationCheckpoints");
	}

	[Fact]
	public void Validate_Fails_When_ProbationCheckpoints_Are_Out_Of_Order()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with
		{
			ProbationMonths = 6,
			ProbationCheckpointDay1 = 60,
			ProbationCheckpointDay2 = 30,
			ProbationCheckpointDay3 = null,
		});
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == "ProbationCheckpoints");
	}

	[Fact]
	public void Validate_Fails_When_A_ProbationCheckpoint_Equals_The_Probation_End_Day()
	{
		// ProbationMonths = 6 -> end day = 180. Exactly 180 must fail (strictly less-than required).
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with
		{
			ProbationMonths = 6,
			ProbationCheckpointDay1 = 30,
			ProbationCheckpointDay2 = 60,
			ProbationCheckpointDay3 = 180,
		});
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == "ProbationCheckpoints");
	}

	[Fact]
	public void Validate_Passes_When_A_ProbationCheckpoint_Is_One_Day_Before_The_Probation_End_Day()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with
		{
			ProbationMonths = 6,
			ProbationCheckpointDay1 = 30,
			ProbationCheckpointDay2 = 60,
			ProbationCheckpointDay3 = 179,
		});
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Fails_When_A_ProbationCheckpoint_Exceeds_The_Probation_End_Day()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with
		{
			ProbationMonths = 6,
			ProbationCheckpointDay1 = 30,
			ProbationCheckpointDay2 = 200,
			ProbationCheckpointDay3 = null,
		});
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == "ProbationCheckpoints");
	}

	// SET-04: attendance-alert thresholds.

	[Theory]
	[InlineData(1)]
	[InlineData(50)]
	public void Validate_Passes_For_FrequentAbsenceCountThreshold_At_Boundaries(int threshold)
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { FrequentAbsenceCountThreshold = threshold });
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Fails_When_FrequentAbsenceCountThreshold_Is_Zero()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { FrequentAbsenceCountThreshold = 0 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.FrequentAbsenceCountThreshold));
	}

	[Fact]
	public void Validate_Fails_When_FrequentAbsenceCountThreshold_Exceeds_Fifty()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { FrequentAbsenceCountThreshold = 51 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.FrequentAbsenceCountThreshold));
	}

	[Theory]
	[InlineData(30)]
	[InlineData(730)]
	public void Validate_Passes_For_FrequentAbsenceWindowDays_At_Boundaries(int windowDays)
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { FrequentAbsenceWindowDays = windowDays });
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Fails_When_FrequentAbsenceWindowDays_Is_Below_Thirty()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { FrequentAbsenceWindowDays = 29 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.FrequentAbsenceWindowDays));
	}

	[Fact]
	public void Validate_Fails_When_FrequentAbsenceWindowDays_Exceeds_SevenHundredThirty()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { FrequentAbsenceWindowDays = 731 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.FrequentAbsenceWindowDays));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(365)]
	public void Validate_Passes_For_LongAbsenceDayThreshold_At_Boundaries(int threshold)
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { LongAbsenceDayThreshold = threshold });
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Fails_When_LongAbsenceDayThreshold_Is_Zero()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { LongAbsenceDayThreshold = 0 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.LongAbsenceDayThreshold));
	}

	[Fact]
	public void Validate_Fails_When_LongAbsenceDayThreshold_Exceeds_ThreeHundredSixtyFive()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { LongAbsenceDayThreshold = 366 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.LongAbsenceDayThreshold));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(50)]
	public void Validate_Passes_For_WeekdayPatternOccurrenceThreshold_At_Boundaries(int threshold)
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { WeekdayPatternOccurrenceThreshold = threshold });
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Fails_When_WeekdayPatternOccurrenceThreshold_Is_Zero()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { WeekdayPatternOccurrenceThreshold = 0 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.WeekdayPatternOccurrenceThreshold));
	}

	[Fact]
	public void Validate_Fails_When_WeekdayPatternOccurrenceThreshold_Exceeds_Fifty()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { WeekdayPatternOccurrenceThreshold = 51 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.WeekdayPatternOccurrenceThreshold));
	}

	[Theory]
	[InlineData(30)]
	[InlineData(730)]
	public void Validate_Passes_For_WeekdayPatternWindowDays_At_Boundaries(int windowDays)
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { WeekdayPatternWindowDays = windowDays });
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Validate_Fails_When_WeekdayPatternWindowDays_Is_Below_Thirty()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { WeekdayPatternWindowDays = 29 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.WeekdayPatternWindowDays));
	}

	[Fact]
	public void Validate_Fails_When_WeekdayPatternWindowDays_Exceeds_SevenHundredThirty()
	{
		var validator = new UpdateHrSettingsValidator();
		var result = validator.Validate(ValidRequest() with { WeekdayPatternWindowDays = 731 });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateHrSettingsRequest.WeekdayPatternWindowDays));
	}
}
