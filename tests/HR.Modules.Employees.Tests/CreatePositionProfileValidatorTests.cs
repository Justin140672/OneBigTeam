using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
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
            LocationId = Guid.NewGuid(),
            DefaultLeavePolicyId = Guid.NewGuid(),
            Title = "Software Developer",
            Description = "Builds software"
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_DepartmentId_Is_Empty()
    {
        var validator = new CreatePositionProfileValidator();

        var result = validator.Validate(new CreatePositionProfileRequest
        {
            CompanyId = Guid.NewGuid(),
            DepartmentId = Guid.Empty,
            LocationId = Guid.NewGuid(),
            DefaultLeavePolicyId = Guid.NewGuid(),
            Title = "Software Developer"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePositionProfileRequest.DepartmentId));
    }

    [Fact]
    public void Validate_Fails_When_LocationId_Is_Empty()
    {
        var validator = new CreatePositionProfileValidator();

        var result = validator.Validate(new CreatePositionProfileRequest
        {
            CompanyId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            LocationId = Guid.Empty,
            DefaultLeavePolicyId = Guid.NewGuid(),
            Title = "Software Developer"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePositionProfileRequest.LocationId));
    }

    [Fact]
    public void Validate_Fails_When_DefaultLeavePolicyId_Is_Empty()
    {
        var validator = new CreatePositionProfileValidator();

        var result = validator.Validate(new CreatePositionProfileRequest
        {
            CompanyId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            DefaultLeavePolicyId = Guid.Empty,
            Title = "Software Developer"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePositionProfileRequest.DefaultLeavePolicyId));
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
            DepartmentId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            Title = "Software Developer",
            ProbationMonthsOverride = 3,
            WorkingDaysOverride = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday | WorkingDays.Thursday,
            HoursPerDayOverride = 8m,
            SalaryMin = 40000,
            SalaryMax = 60000,
            SalaryType = SalaryType.Annual,
            DefaultLeavePolicyId = Guid.NewGuid()
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_SalaryType_Is_Null()
    {
        var validator = new CreatePositionProfileValidator();

        var result = validator.Validate(new CreatePositionProfileRequest
        {
            CompanyId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            DefaultLeavePolicyId = Guid.NewGuid(),
            Title = "Software Developer",
            SalaryType = null
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_SalaryType_Is_Invalid_Enum_Value()
    {
        var validator = new CreatePositionProfileValidator();

        var result = validator.Validate(new CreatePositionProfileRequest
        {
            CompanyId = Guid.NewGuid(),
            Title = "Software Developer",
            SalaryType = (SalaryType)999
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePositionProfileRequest.SalaryType));
    }

    [Fact]
    public void Validate_Fails_When_NoticePeriodUnitOverride_Set_Without_Length()
    {
        var validator = new CreatePositionProfileValidator();

        var result = validator.Validate(new CreatePositionProfileRequest
        {
            CompanyId = Guid.NewGuid(),
            Title = "Software Developer",
            NoticePeriodUnitOverride = NoticePeriodUnit.Weeks,
            NoticePeriodLengthOverride = null
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Notice period unit and length overrides must both be set or both be empty.");
    }

    [Fact]
    public void Validate_Fails_When_NoticePeriodLengthOverride_Set_Without_Unit()
    {
        var validator = new CreatePositionProfileValidator();

        var result = validator.Validate(new CreatePositionProfileRequest
        {
            CompanyId = Guid.NewGuid(),
            Title = "Software Developer",
            NoticePeriodUnitOverride = null,
            NoticePeriodLengthOverride = 4
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Notice period unit and length overrides must both be set or both be empty.");
    }

    [Fact]
    public void Validate_Passes_When_NoticePeriodOverrides_Are_Both_Null()
    {
        var validator = new CreatePositionProfileValidator();

        var result = validator.Validate(new CreatePositionProfileRequest
        {
            CompanyId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            DefaultLeavePolicyId = Guid.NewGuid(),
            Title = "Software Developer",
            NoticePeriodUnitOverride = null,
            NoticePeriodLengthOverride = null
        });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(NoticePeriodUnit.Weeks, 4)]
    [InlineData(NoticePeriodUnit.Months, 1)]
    public void Validate_Passes_When_NoticePeriodOverrides_Are_Both_Set(NoticePeriodUnit unit, int length)
    {
        var validator = new CreatePositionProfileValidator();

        var result = validator.Validate(new CreatePositionProfileRequest
        {
            CompanyId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            DefaultLeavePolicyId = Guid.NewGuid(),
            Title = "Software Developer",
            NoticePeriodUnitOverride = unit,
            NoticePeriodLengthOverride = length
        });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_Fails_When_NoticePeriodLengthOverride_Is_Not_Greater_Than_Zero(int length)
    {
        var validator = new CreatePositionProfileValidator();

        var result = validator.Validate(new CreatePositionProfileRequest
        {
            CompanyId = Guid.NewGuid(),
            Title = "Software Developer",
            NoticePeriodUnitOverride = NoticePeriodUnit.Weeks,
            NoticePeriodLengthOverride = length
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "NoticePeriodLengthOverride must be greater than 0.");
    }
}
