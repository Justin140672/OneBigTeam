using HR.Modules.Companies.Features.GetCustomerBillingHistory;

namespace HR.Modules.Companies.Tests;

public class GetCustomerBillingHistoryValidatorTests
{
    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = new GetCustomerBillingHistoryValidator()
            .Validate(new GetCustomerBillingHistoryRequest(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetCustomerBillingHistoryRequest.CompanyId));
    }

    [Fact]
    public void Validate_Passes_When_CompanyId_Is_Not_Empty()
    {
        var result = new GetCustomerBillingHistoryValidator()
            .Validate(new GetCustomerBillingHistoryRequest(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
