using HR.Modules.Employees.Features.UpdateDepartment;

namespace HR.Modules.Employees.Tests;

public class UpdateDepartmentValidatorTests
{
    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new UpdateDepartmentValidator();

        var result = validator.Validate(new UpdateDepartmentRequest
        {
            CompanyId = Guid.Empty,
            Id = Guid.NewGuid(),
            Name = "Engineering"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateDepartmentRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var validator = new UpdateDepartmentValidator();

        var result = validator.Validate(new UpdateDepartmentRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.Empty,
            Name = "Engineering"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateDepartmentRequest.Id));
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var validator = new UpdateDepartmentValidator();

        var result = validator.Validate(new UpdateDepartmentRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            Name = string.Empty
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateDepartmentRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Name_Exceeds_Max_Length()
    {
        var validator = new UpdateDepartmentValidator();

        var result = validator.Validate(new UpdateDepartmentRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            Name = new string('A', 201)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateDepartmentRequest.Name));
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new UpdateDepartmentValidator();

        var result = validator.Validate(new UpdateDepartmentRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            Name = "Engineering",
            Description = "Builds the product",
            ParentDepartmentId = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid()
        });

        Assert.True(result.IsValid);
    }
}
