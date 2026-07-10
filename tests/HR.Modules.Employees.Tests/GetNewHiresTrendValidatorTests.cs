using HR.Modules.Employees.Features.GetNewHiresTrend;

namespace HR.Modules.Employees.Tests;

public class GetNewHiresTrendValidatorTests
{
    private readonly GetNewHiresTrendValidator _validator = new();

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new GetNewHiresTrendRequest(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetNewHiresTrendRequest.CompanyId));
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new GetNewHiresTrendRequest(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
