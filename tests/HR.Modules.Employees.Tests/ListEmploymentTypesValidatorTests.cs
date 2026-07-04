using HR.Modules.Employees.Features.ListEmploymentTypes;

namespace HR.Modules.Employees.Tests;

public class ListEmploymentTypesValidatorTests
{
    private static readonly ListEmploymentTypesValidator Validator = new();

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var result = Validator.Validate(new ListEmploymentTypesRequest { CompanyId = Guid.NewGuid() });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var result = Validator.Validate(new ListEmploymentTypesRequest { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListEmploymentTypesRequest.CompanyId));
    }

    [Fact]
    public void Validate_Passes_When_IsActive_Is_Null()
    {
        var result = Validator.Validate(new ListEmploymentTypesRequest { CompanyId = Guid.NewGuid(), IsActive = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_IsActive_Is_Specified()
    {
        var result = Validator.Validate(new ListEmploymentTypesRequest { CompanyId = Guid.NewGuid(), IsActive = true });
        Assert.True(result.IsValid);
    }
}
