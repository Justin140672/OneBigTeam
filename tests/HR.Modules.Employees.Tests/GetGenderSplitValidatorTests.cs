using HR.Modules.Employees.Features.GetGenderSplit;

namespace HR.Modules.Employees.Tests;

public class GetGenderSplitValidatorTests
{
    private readonly GetGenderSplitValidator _validator = new();

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new GetGenderSplitRequest(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetGenderSplitRequest.CompanyId));
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new GetGenderSplitRequest(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
