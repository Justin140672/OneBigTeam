using HR.Modules.Employees.Features.UpdateMyContactDetails;

namespace HR.Modules.Employees.Tests;

public class UpdateMyContactDetailsValidatorTests
{
    private static UpdateMyContactDetailsRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        AddressLine1 = "1 Test Street",
        City = "London",
        PostCode = "SW1A 1AA",
        Country = "United Kingdom"
    };

    [Fact]
    public void Validate_Fails_When_AddressLine1_Is_Empty()
    {
        var v = new UpdateMyContactDetailsValidator();
        var result = v.Validate(ValidRequest() with { AddressLine1 = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateMyContactDetailsRequest.AddressLine1));
    }

    [Fact]
    public void Validate_Fails_When_AddressLine1_Is_Null()
    {
        var v = new UpdateMyContactDetailsValidator();
        var result = v.Validate(ValidRequest() with { AddressLine1 = null });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateMyContactDetailsRequest.AddressLine1));
    }

    [Fact]
    public void Validate_Fails_When_City_Is_Empty()
    {
        var v = new UpdateMyContactDetailsValidator();
        var result = v.Validate(ValidRequest() with { City = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateMyContactDetailsRequest.City));
    }

    [Fact]
    public void Validate_Fails_When_City_Is_Null()
    {
        var v = new UpdateMyContactDetailsValidator();
        var result = v.Validate(ValidRequest() with { City = null });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateMyContactDetailsRequest.City));
    }

    [Fact]
    public void Validate_Fails_When_PostCode_Is_Empty()
    {
        var v = new UpdateMyContactDetailsValidator();
        var result = v.Validate(ValidRequest() with { PostCode = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateMyContactDetailsRequest.PostCode));
    }

    [Fact]
    public void Validate_Fails_When_PostCode_Is_Null()
    {
        var v = new UpdateMyContactDetailsValidator();
        var result = v.Validate(ValidRequest() with { PostCode = null });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateMyContactDetailsRequest.PostCode));
    }

    [Fact]
    public void Validate_Fails_When_Country_Is_Empty()
    {
        var v = new UpdateMyContactDetailsValidator();
        var result = v.Validate(ValidRequest() with { Country = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateMyContactDetailsRequest.Country));
    }

    [Fact]
    public void Validate_Fails_When_Country_Is_Null()
    {
        var v = new UpdateMyContactDetailsValidator();
        var result = v.Validate(ValidRequest() with { Country = null });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateMyContactDetailsRequest.Country));
    }

    [Fact]
    public void Validate_Fails_When_PersonalEmail_Is_Invalid()
    {
        var v = new UpdateMyContactDetailsValidator();
        var result = v.Validate(ValidRequest() with { PersonalEmail = "not-an-email" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateMyContactDetailsRequest.PersonalEmail));
    }

    [Fact]
    public void Validate_Passes_When_PersonalEmail_Is_Null()
    {
        var v = new UpdateMyContactDetailsValidator();
        var result = v.Validate(ValidRequest() with { PersonalEmail = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_PhoneNumber_Exceeds_30_Characters()
    {
        var v = new UpdateMyContactDetailsValidator();
        var result = v.Validate(ValidRequest() with { PhoneNumber = new string('0', 31) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateMyContactDetailsRequest.PhoneNumber));
    }

    [Fact]
    public void Validate_Passes_When_PhoneNumber_Is_Null()
    {
        var v = new UpdateMyContactDetailsValidator();
        var result = v.Validate(ValidRequest() with { PhoneNumber = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_HomePhone_Exceeds_30_Characters()
    {
        var v = new UpdateMyContactDetailsValidator();
        var result = v.Validate(ValidRequest() with { HomePhone = new string('0', 31) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateMyContactDetailsRequest.HomePhone));
    }

    [Fact]
    public void Validate_Passes_For_Valid_Minimal_Request()
    {
        var v = new UpdateMyContactDetailsValidator();
        Assert.True(v.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_Passes_For_Full_Valid_Request()
    {
        var v = new UpdateMyContactDetailsValidator();
        var result = v.Validate(new UpdateMyContactDetailsRequest
        {
            CompanyId = Guid.NewGuid(),
            PersonalEmail = "personal@example.com",
            PhoneNumber = "07700 900001",
            HomePhone = "01234 567890",
            AddressLine1 = "42 Acacia Avenue",
            AddressLine2 = "Flat 3",
            City = "Manchester",
            County = "Greater Manchester",
            PostCode = "M1 1AA",
            Country = "United Kingdom"
        });
        Assert.True(result.IsValid);
    }
}
