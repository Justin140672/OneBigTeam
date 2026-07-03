using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.UpdateCompany;

namespace HR.Modules.Companies.Tests;

public class UpdateCompanyValidatorTests
{
    private static UpdateCompanyAddressRequest ValidAddress() => new()
    {
        Type = CompanyAddressType.RegisteredOffice,
        Line1 = "1 Business Park",
        City = "London",
        CountryCode = "GB"
    };

    private static UpdateCompanyRequest ValidRequest() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Acme Corp",
        Addresses = [ValidAddress()]
    };

    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var v = new UpdateCompanyValidator();
        var result = v.Validate(ValidRequest() with { Id = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCompanyRequest.Id));
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var v = new UpdateCompanyValidator();
        var result = v.Validate(ValidRequest() with { Name = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCompanyRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Name_Exceeds_200_Characters()
    {
        var v = new UpdateCompanyValidator();
        var result = v.Validate(ValidRequest() with { Name = new string('N', 201) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCompanyRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_No_Registered_Office_Address()
    {
        var v = new UpdateCompanyValidator();
        var result = v.Validate(ValidRequest() with
        {
            Addresses = [ValidAddress() with { Type = CompanyAddressType.TradingAddress }]
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCompanyRequest.Addresses)
                                         && e.ErrorMessage.Contains("Registered Office"));
    }

    [Fact]
    public void Validate_Fails_When_Duplicate_Address_Types()
    {
        var v = new UpdateCompanyValidator();
        var result = v.Validate(ValidRequest() with
        {
            Addresses = [ValidAddress(), ValidAddress()]
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCompanyRequest.Addresses)
                                         && e.ErrorMessage.Contains("unique"));
    }

    [Fact]
    public void Validate_Fails_When_Address_Line1_Is_Empty()
    {
        var v = new UpdateCompanyValidator();
        var result = v.Validate(ValidRequest() with
        {
            Addresses = [ValidAddress() with { Line1 = string.Empty }]
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("Line1"));
    }

    [Fact]
    public void Validate_Fails_When_Address_Line1_Exceeds_200_Characters()
    {
        var v = new UpdateCompanyValidator();
        var result = v.Validate(ValidRequest() with
        {
            Addresses = [ValidAddress() with { Line1 = new string('A', 201) }]
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("Line1"));
    }

    [Fact]
    public void Validate_Fails_When_Address_City_Is_Empty()
    {
        var v = new UpdateCompanyValidator();
        var result = v.Validate(ValidRequest() with
        {
            Addresses = [ValidAddress() with { City = string.Empty }]
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("City"));
    }

    [Fact]
    public void Validate_Fails_When_CountryCode_Is_Empty()
    {
        var v = new UpdateCompanyValidator();
        var result = v.Validate(ValidRequest() with
        {
            Addresses = [ValidAddress() with { CountryCode = string.Empty }]
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("CountryCode"));
    }

    [Fact]
    public void Validate_Fails_When_CountryCode_Is_Not_Two_Characters()
    {
        var v = new UpdateCompanyValidator();
        var result = v.Validate(ValidRequest() with
        {
            Addresses = [ValidAddress() with { CountryCode = "GBR" }]
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("CountryCode"));
    }

    [Fact]
    public void Validate_Fails_When_CountryCode_Contains_Digits()
    {
        var v = new UpdateCompanyValidator();
        var result = v.Validate(ValidRequest() with
        {
            Addresses = [ValidAddress() with { CountryCode = "G1" }]
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("CountryCode"));
    }

    [Fact]
    public void Validate_Passes_With_Multiple_Valid_Addresses()
    {
        var v = new UpdateCompanyValidator();
        var result = v.Validate(ValidRequest() with
        {
            Addresses =
            [
                ValidAddress(),
                new UpdateCompanyAddressRequest
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
    public void Validate_Passes_For_Valid_Minimal_Request()
    {
        var v = new UpdateCompanyValidator();
        Assert.True(v.Validate(ValidRequest()).IsValid);
    }
}
