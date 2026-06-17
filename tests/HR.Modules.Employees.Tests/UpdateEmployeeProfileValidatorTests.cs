using HR.Modules.Employees.Features.UpdateEmployeeProfile;

namespace HR.Modules.Employees.Tests;

public class UpdateEmployeeProfileValidatorTests
{
    private static UpdateEmployeeProfileRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        Id = Guid.NewGuid(),
        FirstName = "Alice",
        LastName = "Smith",
        WorkEmail = "alice@example.com",
        StartDate = new DateOnly(2026, 7, 1)
    };

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var v = new UpdateEmployeeProfileValidator();
        var result = v.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmployeeProfileRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var v = new UpdateEmployeeProfileValidator();
        var result = v.Validate(ValidRequest() with { Id = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmployeeProfileRequest.Id));
    }

    [Fact]
    public void Validate_Fails_When_FirstName_Is_Empty()
    {
        var v = new UpdateEmployeeProfileValidator();
        var result = v.Validate(ValidRequest() with { FirstName = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmployeeProfileRequest.FirstName));
    }

    [Fact]
    public void Validate_Fails_When_FirstName_Exceeds_100_Characters()
    {
        var v = new UpdateEmployeeProfileValidator();
        var result = v.Validate(ValidRequest() with { FirstName = new string('A', 101) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmployeeProfileRequest.FirstName));
    }

    [Fact]
    public void Validate_Fails_When_LastName_Is_Empty()
    {
        var v = new UpdateEmployeeProfileValidator();
        var result = v.Validate(ValidRequest() with { LastName = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmployeeProfileRequest.LastName));
    }

    [Fact]
    public void Validate_Fails_When_LastName_Exceeds_100_Characters()
    {
        var v = new UpdateEmployeeProfileValidator();
        var result = v.Validate(ValidRequest() with { LastName = new string('B', 101) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmployeeProfileRequest.LastName));
    }

    [Fact]
    public void Validate_Fails_When_WorkEmail_Is_Empty()
    {
        var v = new UpdateEmployeeProfileValidator();
        var result = v.Validate(ValidRequest() with { WorkEmail = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmployeeProfileRequest.WorkEmail));
    }

    [Fact]
    public void Validate_Fails_When_WorkEmail_Is_Invalid()
    {
        var v = new UpdateEmployeeProfileValidator();
        var result = v.Validate(ValidRequest() with { WorkEmail = "not-an-email" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmployeeProfileRequest.WorkEmail));
    }

    [Fact]
    public void Validate_Fails_When_WorkEmail_Exceeds_320_Characters()
    {
        var v = new UpdateEmployeeProfileValidator();
        var result = v.Validate(ValidRequest() with { WorkEmail = new string('a', 315) + "@x.com" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmployeeProfileRequest.WorkEmail));
    }

    [Fact]
    public void Validate_Fails_When_PersonalEmail_Is_Invalid()
    {
        var v = new UpdateEmployeeProfileValidator();
        var result = v.Validate(ValidRequest() with { PersonalEmail = "bad-email" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmployeeProfileRequest.PersonalEmail));
    }

    [Fact]
    public void Validate_Passes_When_PersonalEmail_Is_Null()
    {
        var v = new UpdateEmployeeProfileValidator();
        var result = v.Validate(ValidRequest() with { PersonalEmail = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_StartDate_Is_Default()
    {
        var v = new UpdateEmployeeProfileValidator();
        var result = v.Validate(ValidRequest() with { StartDate = default });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmployeeProfileRequest.StartDate));
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var v = new UpdateEmployeeProfileValidator();
        Assert.True(v.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_Passes_For_Full_Valid_Request()
    {
        var v = new UpdateEmployeeProfileValidator();
        var result = v.Validate(ValidRequest() with
        {
            DepartmentId = Guid.NewGuid(),
            PositionProfileId = Guid.NewGuid(),
            PreferredName = "Al",
            PersonalEmail = "alice.personal@gmail.com"
        });
        Assert.True(result.IsValid);
    }
}
