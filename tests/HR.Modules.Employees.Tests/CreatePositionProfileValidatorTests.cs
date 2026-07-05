using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Features.CreatePositionProfile;

namespace HR.Modules.Employees.Tests;

public class CreatePositionProfileValidatorTests
{
    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new CreatePositionProfileValidator();

        var result = validator.Validate(new CreatePositionProfileRequest
        {
            CompanyId = Guid.Empty,
            Title = "Software Developer"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePositionProfileRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Title_Is_Empty()
    {
        var validator = new CreatePositionProfileValidator();

        var result = validator.Validate(new CreatePositionProfileRequest
        {
            CompanyId = Guid.NewGuid(),
            Title = string.Empty
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePositionProfileRequest.Title));
    }

    [Fact]
    public void Validate_Fails_When_Title_Exceeds_Max_Length()
    {
        var validator = new CreatePositionProfileValidator();

        var result = validator.Validate(new CreatePositionProfileRequest
        {
            CompanyId = Guid.NewGuid(),
            Title = new string('A', 201)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePositionProfileRequest.Title));
    }

    [Fact]
    public void Validate_Fails_When_Description_Exceeds_Max_Length()
    {
        var validator = new CreatePositionProfileValidator();

        var result = validator.Validate(new CreatePositionProfileRequest
        {
            CompanyId = Guid.NewGuid(),
            Title = "Software Developer",
            Description = new string('A', 2001)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePositionProfileRequest.Description));
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new CreatePositionProfileValidator();

        var result = validator.Validate(new CreatePositionProfileRequest
        {
            CompanyId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            Title = "Software Developer",
            Description = "Builds software",
            IsManagerial = false
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_WorkingDaysOverride_Is_None()
    {
        var validator = new CreatePositionProfileValidator();

        var result = validator.Validate(new CreatePositionProfileRequest
        {
            CompanyId = Guid.NewGuid(),
            Title = "Software Developer",
            WorkingDaysOverride = WorkingDays.None,
            HoursPerDayOverride = 7.5m
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Working days override must include at least one day.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(24.5)]
    public void Validate_Fails_When_HoursPerDayOverride_Out_Of_Range(decimal hours)
    {
        var validator = new CreatePositionProfileValidator();

        var result = validator.Validate(new CreatePositionProfileRequest
        {
            CompanyId = Guid.NewGuid(),
            Title = "Software Developer",
            HoursPerDayOverride = hours
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("HoursPerDayOverride"));
    }

    [Fact]
    public void Validate_Fails_When_SalaryMax_Less_Than_SalaryMin()
    {
        var validator = new CreatePositionProfileValidator();

        var result = validator.Validate(new CreatePositionProfileRequest
        {
            CompanyId = Guid.NewGuid(),
            Title = "Software Developer",
            SalaryMin = 50000,
            SalaryMax = 40000
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePositionProfileRequest.SalaryMax));
    }

    [Fact]
    public void Validate_Passes_With_Full_Set_Of_New_Fields()
    {
        var validator = new CreatePositionProfileValidator();

        var result = validator.Validate(new CreatePositionProfileRequest
        {
            CompanyId = Guid.NewGuid(),
            Title = "Software Developer",
            ProbationMonthsOverride = 3,
            WorkingDaysOverride = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday | WorkingDays.Thursday,
            HoursPerDayOverride = 8m,
            SalaryMin = 40000,
            SalaryMax = 60000,
            DefaultLeavePolicyId = Guid.NewGuid()
        });

        Assert.True(result.IsValid);
    }
}
