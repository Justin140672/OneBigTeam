using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.UpdatePositionProfile;

namespace HR.Modules.Employees.Tests;

public class UpdatePositionProfileValidatorTests
{
    private static UpdatePositionProfileRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        Id = Guid.NewGuid(),
        Title = "Senior Developer"
    };

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var v = new UpdatePositionProfileValidator();
        var result = v.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdatePositionProfileRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var v = new UpdatePositionProfileValidator();
        var result = v.Validate(ValidRequest() with { Id = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdatePositionProfileRequest.Id));
    }

    [Fact]
    public void Validate_Fails_When_Title_Is_Empty()
    {
        var v = new UpdatePositionProfileValidator();
        var result = v.Validate(ValidRequest() with { Title = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdatePositionProfileRequest.Title));
    }

    [Fact]
    public void Validate_Fails_When_Title_Exceeds_200_Characters()
    {
        var v = new UpdatePositionProfileValidator();
        var result = v.Validate(ValidRequest() with { Title = new string('T', 201) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdatePositionProfileRequest.Title));
    }

    [Fact]
    public void Validate_Fails_When_Description_Exceeds_2000_Characters()
    {
        var v = new UpdatePositionProfileValidator();
        var result = v.Validate(ValidRequest() with { Description = new string('D', 2001) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdatePositionProfileRequest.Description));
    }

    [Fact]
    public void Validate_Passes_When_Description_Is_Null()
    {
        var v = new UpdatePositionProfileValidator();
        var result = v.Validate(ValidRequest() with { Description = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Description_Is_At_Max_Length()
    {
        var v = new UpdatePositionProfileValidator();
        var result = v.Validate(ValidRequest() with { Description = new string('D', 2000) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_For_Valid_Minimal_Request()
    {
        var v = new UpdatePositionProfileValidator();
        Assert.True(v.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_Passes_For_Full_Valid_Request()
    {
        var v = new UpdatePositionProfileValidator();
        var result = v.Validate(ValidRequest() with
        {
            DepartmentId = Guid.NewGuid(),
            Description = "Builds and maintains the core platform."
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_WorkingDaysOverride_Is_None()
    {
        var v = new UpdatePositionProfileValidator();
        var result = v.Validate(ValidRequest() with { WorkingDaysOverride = WorkingDays.None, HoursPerDayOverride = 7.5m });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Working days override must include at least one day.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(24.5)]
    public void Validate_Fails_When_HoursPerDayOverride_Out_Of_Range(decimal hours)
    {
        var v = new UpdatePositionProfileValidator();
        var result = v.Validate(ValidRequest() with { HoursPerDayOverride = hours });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("HoursPerDayOverride"));
    }

    [Fact]
    public void Validate_Fails_When_SalaryMax_Less_Than_SalaryMin()
    {
        var v = new UpdatePositionProfileValidator();
        var result = v.Validate(ValidRequest() with { SalaryMin = 50000, SalaryMax = 40000 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdatePositionProfileRequest.SalaryMax));
    }

    [Fact]
    public void Validate_Passes_With_Full_Set_Of_New_Fields()
    {
        var v = new UpdatePositionProfileValidator();
        var result = v.Validate(ValidRequest() with
        {
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
        var v = new UpdatePositionProfileValidator();
        var result = v.Validate(ValidRequest() with { SalaryType = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_SalaryType_Is_Invalid_Enum_Value()
    {
        var v = new UpdatePositionProfileValidator();
        var result = v.Validate(ValidRequest() with { SalaryType = (SalaryType)999 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdatePositionProfileRequest.SalaryType));
    }
}
