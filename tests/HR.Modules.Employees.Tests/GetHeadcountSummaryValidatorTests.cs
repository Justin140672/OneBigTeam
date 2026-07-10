using HR.Modules.Employees.Features.GetHeadcountSummary;

namespace HR.Modules.Employees.Tests;

public class GetHeadcountSummaryValidatorTests
{
    private readonly GetHeadcountSummaryValidator _validator = new();

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new GetHeadcountSummaryRequest(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetHeadcountSummaryRequest.CompanyId));
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new GetHeadcountSummaryRequest(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
