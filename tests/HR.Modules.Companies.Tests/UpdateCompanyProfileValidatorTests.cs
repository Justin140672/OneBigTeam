using HR.Modules.Companies.Features.UpdateCompanyProfile;

using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Tests;

public class UpdateCompanyProfileValidatorTests
{
    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var validator = new UpdateCompanyProfileValidator();

        var result = validator.Validate(new UpdateCompanyProfileRequest
        {
            Id = Guid.NewGuid(),
            Name = string.Empty
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateCompanyProfileRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Address_CountryCode_Is_Invalid()
    {
        var validator = new UpdateCompanyProfileValidator();

        var result = validator.Validate(new UpdateCompanyProfileRequest
        {
            Id = Guid.NewGuid(),
            Name = "Acme",
            Addresses =
            [
                new UpdateCompanyAddressRequest
                {
                    Type = CompanyAddressType.RegisteredOffice,
                    Line1 = "1 Main Road",
                    City = "London",
                    CountryCode = "GBR"
                }
            ]
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName.Contains(nameof(UpdateCompanyAddressRequest.CountryCode), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Fails_When_Registered_Office_Is_Missing()
    {
        var validator = new UpdateCompanyProfileValidator();

        var result = validator.Validate(new UpdateCompanyProfileRequest
        {
            Id = Guid.NewGuid(),
            Name = "Acme",
            Addresses =
            [
                new UpdateCompanyAddressRequest
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
