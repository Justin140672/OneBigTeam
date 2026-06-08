using HR.Modules.Employees.Features.AssignManager;

namespace HR.Modules.Employees.Tests;

public class AssignManagerValidatorTests
{
    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new AssignManagerValidator();

        var result = validator.Validate(new AssignManagerRequest
        {
            CompanyId = Guid.Empty,
            Id = Guid.NewGuid()
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AssignManagerRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_ManagerId_Equals_Id()
    {
        var id = Guid.NewGuid();
        var validator = new AssignManagerValidator();

        var result = validator.Validate(new AssignManagerRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = id,
            ManagerId = id
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AssignManagerRequest.ManagerId));
    }

    [Fact]
    public void Validate_Passes_With_Valid_Manager()
    {
        var validator = new AssignManagerValidator();

        var result = validator.Validate(new AssignManagerRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerId = Guid.NewGuid()
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_With_Null_Manager()
    {
        var validator = new AssignManagerValidator();

        var result = validator.Validate(new AssignManagerRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerId = null
        });

        Assert.True(result.IsValid);
    }
}
