using HR.Modules.Companies.Features.GetCustomerBillingBreakdown;

namespace HR.Modules.Companies.Tests;

public class GetCustomerBillingBreakdownValidatorTests
{
    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = new GetCustomerBillingBreakdownValidator()
            .Validate(new GetCustomerBillingBreakdownRequest(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetCustomerBillingBreakdownRequest.CompanyId));
    }

    [Fact]
    public void Validate_Passes_When_CompanyId_Is_Not_Empty()
    {
        var result = new GetCustomerBillingBreakdownValidator()
            .Validate(new GetCustomerBillingBreakdownRequest(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
