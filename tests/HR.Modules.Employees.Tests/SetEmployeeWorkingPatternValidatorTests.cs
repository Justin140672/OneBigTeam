using HR.Modules.Employees.Features.SetEmployeeWorkingPattern;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Employees.Tests;

public class SetEmployeeWorkingPatternValidatorTests
{
    private readonly SetEmployeeWorkingPatternValidator _validator = new();

    [Fact]
    public void Validate_Passes_When_Both_Overrides_Are_Null()
    {
        var result = _validator.Validate(new SetEmployeeWorkingPatternRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            WorkingDaysOverride = null,
            HoursPerDayOverride = null
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_With_Valid_Working_Days_And_Hours()
    {
        var result = _validator.Validate(new SetEmployeeWorkingPatternRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            WorkingDaysOverride = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday,
            HoursPerDayOverride = 7.5m
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new SetEmployeeWorkingPatternRequest
        {
            CompanyId = Guid.Empty,
            EmployeeId = Guid.NewGuid()
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SetEmployeeWorkingPatternRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var result = _validator.Validate(new SetEmployeeWorkingPatternRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.Empty
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SetEmployeeWorkingPatternRequest.EmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_WorkingDaysOverride_Is_None()
    {
        var result = _validator.Validate(new SetEmployeeWorkingPatternRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            WorkingDaysOverride = WorkingDays.None
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Working days override must include at least one day"));
    }

    [Fact]
    public void Validate_Fails_When_HoursPerDayOverride_Is_Zero()
    {
        var result = _validator.Validate(new SetEmployeeWorkingPatternRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            HoursPerDayOverride = 0m
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("greater than"));
    }

    [Fact]
    public void Validate_Fails_When_HoursPerDayOverride_Exceeds_24()
    {
        var result = _validator.Validate(new SetEmployeeWorkingPatternRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            HoursPerDayOverride = 24.1m
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("less than or equal to"));
    }

    [Fact]
    public void Validate_Passes_When_HoursPerDayOverride_Is_Exactly_24()
    {
        var result = _validator.Validate(new SetEmployeeWorkingPatternRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            HoursPerDayOverride = 24m
        });

        Assert.True(result.IsValid);
    }
}
