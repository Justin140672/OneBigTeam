using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.UpdateEmploymentDetails;
using HR.SharedKernel;

namespace HR.Modules.Employees.Tests;

public class UpdateEmploymentDetailsValidatorTests
{
    private readonly UpdateEmploymentDetailsValidator _validator = new();

    private static UpdateEmploymentDetailsRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        Id = Guid.NewGuid(),
        EmployeeNumber = "EMP-001",
        EmploymentTypeId = Guid.NewGuid(),
        Status = EmploymentStatus.Active,
        StartDate = new DateOnly(2026, 1, 1)
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(ValidRequest());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_EmployeeNumber_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { EmployeeNumber = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentDetailsRequest.EmployeeNumber));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeNumber_Exceeds_50_Characters()
    {
        var result = _validator.Validate(ValidRequest() with { EmployeeNumber = new string('X', 51) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentDetailsRequest.EmployeeNumber));
    }

    [Fact]
    public void Validate_Passes_When_EmploymentTypeId_Is_Null()
    {
        var result = _validator.Validate(ValidRequest() with { EmploymentTypeId = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_EmploymentTypeId_Is_Empty_Guid()
    {
        var result = _validator.Validate(ValidRequest() with { EmploymentTypeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentDetailsRequest.EmploymentTypeId));
    }

    [Fact]
    public void Validate_Fails_When_Status_Is_Draft()
    {
        var result = _validator.Validate(ValidRequest() with { Status = EmploymentStatus.Draft });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentDetailsRequest.Status));
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentDetailsRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { Id = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentDetailsRequest.Id));
    }

    [Fact]
    public void Validate_Fails_When_HoursPerDayOverride_Is_Zero()
    {
        var result = _validator.Validate(ValidRequest() with { HoursPerDayOverride = 0m });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentDetailsRequest.HoursPerDayOverride));
    }

    [Fact]
    public void Validate_Fails_When_HoursPerDayOverride_Exceeds_24()
    {
        var result = _validator.Validate(ValidRequest() with { HoursPerDayOverride = 24.1m });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentDetailsRequest.HoursPerDayOverride));
    }

    [Fact]
    public void Validate_Fails_When_WorkingDaysOverride_Is_None()
    {
        var result = _validator.Validate(ValidRequest() with { WorkingDaysOverride = WorkingDays.None });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentDetailsRequest.WorkingDaysOverride));
    }
}
