using HR.Modules.Employees.Features.CreateDepartment;

namespace HR.Modules.Employees.Tests;

public class CreateDepartmentValidatorTests
{
    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new CreateDepartmentValidator();

        var result = validator.Validate(new CreateDepartmentRequest
        {
            CompanyId = Guid.Empty,
            Name = "Engineering"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateDepartmentRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var validator = new CreateDepartmentValidator();

        var result = validator.Validate(new CreateDepartmentRequest
        {
            CompanyId = Guid.NewGuid(),
            Name = string.Empty
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateDepartmentRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Name_Exceeds_Max_Length()
    {
        var validator = new CreateDepartmentValidator();

        var result = validator.Validate(new CreateDepartmentRequest
        {
            CompanyId = Guid.NewGuid(),
            Name = new string('A', 201)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateDepartmentRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Description_Exceeds_Max_Length()
    {
        var validator = new CreateDepartmentValidator();

        var result = validator.Validate(new CreateDepartmentRequest
        {
            CompanyId = Guid.NewGuid(),
            Name = "Engineering",
            Description = new string('A', 1001)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateDepartmentRequest.Description));
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new CreateDepartmentValidator();

        var result = validator.Validate(new CreateDepartmentRequest
        {
            CompanyId = Guid.NewGuid(),
            Name = "Engineering",
            Description = "Builds the platform",
            ParentDepartmentId = Guid.NewGuid()
        });

        Assert.True(result.IsValid);
    }
}
