using HR.Modules.Companies.Features.GetCustomerSupportView;

namespace HR.Modules.Companies.Tests;

public class GetCustomerSupportViewValidatorTests
{
    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = new GetCustomerSupportViewValidator()
            .Validate(new GetCustomerSupportViewRequest(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetCustomerSupportViewRequest.CompanyId));
    }

    [Fact]
    public void Validate_Passes_When_CompanyId_Is_Not_Empty()
    {
        var result = new GetCustomerSupportViewValidator()
            .Validate(new GetCustomerSupportViewRequest(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
