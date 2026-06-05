using HR.Modules.Companies.Features.CreateCompany;

using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Tests;

public class CreateCompanyValidatorTests
{
    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var validator = new CreateCompanyValidator();

        var result = validator.Validate(new CreateCompanyRequest { Name = string.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateCompanyRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Slug_Format_Is_Invalid()
    {
        var validator = new CreateCompanyValidator();

        var result = validator.Validate(new CreateCompanyRequest
        {
            Name = "Acme Corp",
            Slug = "ACME corp"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateCompanyRequest.Slug));
    }

    [Fact]
    public void Validate_Fails_When_Address_Types_Are_Duplicated()
    {
        var validator = new CreateCompanyValidator();

        var result = validator.Validate(new CreateCompanyRequest
        {
            Name = "Acme Corp",
            Addresses =
            [
                new CreateCompanyAddressRequest
                {
                    Type = CompanyAddressType.RegisteredOffice,
                    Line1 = "1 Main Road",
                    City = "London",
                    CountryCode = "GB"
                },
                new CreateCompanyAddressRequest
                {
                    Type = CompanyAddressType.RegisteredOffice,
                    Line1 = "2 Main Road",
                    City = "London",
                    CountryCode = "GB"
                }
            ]
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateCompanyRequest.Addresses));
    }

    [Fact]
    public void Validate_Fails_When_Registered_Office_Is_Missing()
    {
        var validator = new CreateCompanyValidator();

        var result = validator.Validate(new CreateCompanyRequest
        {
            Name = "Acme Corp",
            Addresses =
            [
                new CreateCompanyAddressRequest
                {
                    Type = CompanyAddressType.TradingAddress,
                    Line1 = "1 Main Road",
                    City = "London",
                    CountryCode = "GB"
                }
            ]
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("Registered Office", StringComparison.Ordinal));
    }
}
