using HR.Modules.Employees.Features.GetEmploymentTypeSplit;

namespace HR.Modules.Employees.Tests;

public class GetEmploymentTypeSplitValidatorTests
{
    private readonly GetEmploymentTypeSplitValidator _validator = new();

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new GetEmploymentTypeSplitRequest(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmploymentTypeSplitRequest.CompanyId));
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new GetEmploymentTypeSplitRequest(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
