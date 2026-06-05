using HR.Modules.Companies.Features.UpdateCompanySettings;
using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Tests;

public class UpdateCompanySettingsValidatorTests
{
    [Fact]
    public void Validate_Fails_When_LeaveYearStartMonth_Is_Out_Of_Range()
    {
        var validator = new UpdateCompanySettingsValidator();

        var result = validator.Validate(new UpdateCompanySettingsRequest
        {
            Id = Guid.NewGuid(),
            TimeZone = "UTC",
            Locale = "en-GB",
            WorkingWeek = WorkingDays.Monday,
            LeaveYearStartMonth = 13,
            DefaultHolidayAllowance = 25m,
            ProbationMonths = 6
        });

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(UpdateCompanySettingsRequest.LeaveYearStartMonth));
    }

    [Fact]
    public void Validate_Succeeds_For_Valid_Request()
    {
        var validator = new UpdateCompanySettingsValidator();

        var result = validator.Validate(new UpdateCompanySettingsRequest
        {
            Id = Guid.NewGuid(),
            TimeZone = "Europe/London",
            Locale = "en-GB",
            WorkingWeek = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday | WorkingDays.Thursday | WorkingDays.Friday,
            LeaveYearStartMonth = 4,
            DefaultHolidayAllowance = 28.5m,
            ProbationMonths = 3
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_WorkingWeek_Is_None()
    {
        var validator = new UpdateCompanySettingsValidator();

        var result = validator.Validate(new UpdateCompanySettingsRequest
        {
            Id = Guid.NewGuid(),
            TimeZone = "Europe/London",
            Locale = "en-GB",
            WorkingWeek = WorkingDays.None,
            LeaveYearStartMonth = 4,
            DefaultHolidayAllowance = 28.5m,
            ProbationMonths = 3
        });

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(UpdateCompanySettingsRequest.WorkingWeek));
    }
}
