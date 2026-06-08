using HR.Modules.Employees.Features.ListDepartments;

namespace HR.Modules.Employees.Tests;

public class ListDepartmentsValidatorTests
{
    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new ListDepartmentsValidator();

        var result = validator.Validate(new ListDepartmentsRequest { CompanyId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListDepartmentsRequest.CompanyId));
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new ListDepartmentsValidator();

        var result = validator.Validate(new ListDepartmentsRequest { CompanyId = Guid.NewGuid() });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_IncludeInactive_Is_True()
    {
        var validator = new ListDepartmentsValidator();

        var result = validator.Validate(new ListDepartmentsRequest
        {
            CompanyId = Guid.NewGuid(),
            IncludeInactive = true
        });

        Assert.True(result.IsValid);
    }
}
