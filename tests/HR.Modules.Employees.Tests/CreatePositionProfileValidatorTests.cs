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
}
