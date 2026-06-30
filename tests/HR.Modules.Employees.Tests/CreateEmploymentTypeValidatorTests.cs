using HR.Modules.Employees.Features.CreateEmploymentType;

namespace HR.Modules.Employees.Tests;

public class CreateEmploymentTypeValidatorTests
{
    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new CreateEmploymentTypeValidator();

        var result = validator.Validate(new CreateEmploymentTypeRequest
        {
            CompanyId = Guid.Empty,
            Name = "Permanent"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmploymentTypeRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var validator = new CreateEmploymentTypeValidator();

        var result = validator.Validate(new CreateEmploymentTypeRequest
        {
            CompanyId = Guid.NewGuid(),
            Name = string.Empty
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmploymentTypeRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Name_Exceeds_Max_Length()
    {
        var validator = new CreateEmploymentTypeValidator();

        var result = validator.Validate(new CreateEmploymentTypeRequest
        {
            CompanyId = Guid.NewGuid(),
            Name = new string('A', 101)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmploymentTypeRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Description_Exceeds_Max_Length()
    {
        var validator = new CreateEmploymentTypeValidator();

        var result = validator.Validate(new CreateEmploymentTypeRequest
        {
            CompanyId = Guid.NewGuid(),
            Name = "Permanent",
            Description = new string('A', 501)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmploymentTypeRequest.Description));
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new CreateEmploymentTypeValidator();

        var result = validator.Validate(new CreateEmploymentTypeRequest
        {
            CompanyId = Guid.NewGuid(),
            Name = "Permanent",
            Description = "Full-time permanent"
        });

        Assert.True(result.IsValid);
    }
}
