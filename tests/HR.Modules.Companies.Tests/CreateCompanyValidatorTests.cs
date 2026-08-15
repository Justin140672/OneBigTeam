using HR.Modules.Companies.Features.CreateCompany;

using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Tests;

public class CreateCompanyValidatorTests
{
    private static CreateCompanyAddressRequest ValidAddress() => new()
    {
        Type = CompanyAddressType.RegisteredOffice,
        Line1 = "1 Business Park",
        City = "London",
        CountryCode = "GB"
    };

    private static CreateCompanyRequest ValidRequest() => new()
    {
        Name = "Acme Corp",
        Addresses = [ValidAddress()]
    };

    [Fact]
    public void Validate_Passes_For_Valid_Minimal_Request()
    {
        var validator = new CreateCompanyValidator();
        Assert.True(validator.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var validator = new CreateCompanyValidator();

        var result = validator.Validate(new CreateCompanyRequest { Name = string.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateCompanyRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Name_Exceeds_200_Characters()
    {
        var validator = new CreateCompanyValidator();

        var result = validator.Validate(ValidRequest() with { Name = new string('N', 201) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateCompanyRequest.Name));
    }

    [Fact]
    public void Validate_Passes_When_Name_Is_At_Maximum_Length()
    {
        var validator = new CreateCompanyValidator();

        var result = validator.Validate(ValidRequest() with { Name = new string('N', 200) });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Address_Line1_Is_Empty()
    {
        var validator = new CreateCompanyValidator();

        var result = validator.Validate(ValidRequest() with { Addresses = [ValidAddress() with { Line1 = string.Empty }] });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("Line1"));
    }

    [Fact]
    public void Validate_Fails_When_Address_Line1_Exceeds_200_Characters()
    {
        var validator = new CreateCompanyValidator();

        var result = validator.Validate(ValidRequest() with { Addresses = [ValidAddress() with { Line1 = new string('A', 201) }] });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("Line1"));
    }

    [Fact]
    public void Validate_Fails_When_Address_City_Is_Empty()
    {
        var validator = new CreateCompanyValidator();

        var result = validator.Validate(ValidRequest() with { Addresses = [ValidAddress() with { City = string.Empty }] });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("City"));
    }

    [Fact]
    public void Validate_Fails_When_CountryCode_Is_Not_Two_Characters()
    {
        var validator = new CreateCompanyValidator();

        var result = validator.Validate(ValidRequest() with { Addresses = [ValidAddress() with { CountryCode = "GBR" }] });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("CountryCode"));
    }

    [Fact]
    public void Validate_Fails_When_CountryCode_Contains_Digits()
    {
        var validator = new CreateCompanyValidator();

        var result = validator.Validate(ValidRequest() with { Addresses = [ValidAddress() with { CountryCode = "G1" }] });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("CountryCode"));
    }

    [Fact]
    public void Validate_Fails_When_Address_Type_Is_Not_A_Defined_Enum_Value()
    {
        var validator = new CreateCompanyValidator();

        var result = validator.Validate(ValidRequest() with { Addresses = [ValidAddress() with { Type = (CompanyAddressType)99 }] });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("Type"));
    }

    [Fact]
    public void Validate_Passes_With_Multiple_Valid_Addresses()
    {
        var validator = new CreateCompanyValidator();

        var result = validator.Validate(ValidRequest() with
        {
            Addresses =
            [
                ValidAddress(),
                new CreateCompanyAddressRequest
                {
                    Type = CompanyAddressType.TradingAddress,
                    Line1 = "2 Trading Place",
                    City = "Manchester",
                    CountryCode = "GB"
                }
            ]
        });

        Assert.True(result.IsValid);
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
