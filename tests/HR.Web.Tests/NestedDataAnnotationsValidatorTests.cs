using HR.Web.Components.Controls;
using HR.Web.Models;

namespace HR.Web.Tests;

public class NestedDataAnnotationsValidatorTests
{
    private const string UkPostcodeRegex = @"^[A-Za-z]{1,2}\d[A-Za-z\d]?\s?\d[A-Za-z]{2}$";

    private static CompanyAddressEditModel ValidAddress() => new()
    {
        Type = "Registered",
        Line1 = "1 Example Street",
        City = "London",
        PostalCode = "SW1A 1AA",
        CountryCode = "GB",
        PostcodeRegexPattern = UkPostcodeRegex
    };

    [Fact]
    public void ValidateNestedObjects_ExcludesRootErrors()
    {
        var model = new CompanyDetailsEditModel { Name = string.Empty, Addresses = [ValidAddress()] };

        var errors = NestedDataAnnotationsValidator.ValidateNestedObjects(model);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateNestedObjects_ReportsRequiredChildErrorsOnce()
    {
        var address = ValidAddress();
        address.Line1 = null;
        address.City = null;
        var model = new CompanyDetailsEditModel { Name = "Acme", Addresses = [address] };

        var errors = NestedDataAnnotationsValidator.ValidateNestedObjects(model);

        Assert.Single(errors, e => e.Message == "Line 1 is required." && e.Field.Model == address && e.Field.FieldName == nameof(address.Line1));
        Assert.Single(errors, e => e.Message == "City is required." && e.Field.Model == address && e.Field.FieldName == nameof(address.City));
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void ValidateNestedObjects_ReportsInvalidPostcodeOnceAndClearsWhenCorrected()
    {
        var address = ValidAddress();
        address.PostalCode = "NOT A POSTCODE";
        var model = new CompanyDetailsEditModel { Name = "Acme", Addresses = [address] };

        var errors = NestedDataAnnotationsValidator.ValidateNestedObjects(model);
        var postcodeError = Assert.Single(errors);
        Assert.Equal("Enter a valid postcode.", postcodeError.Message);
        Assert.Equal(nameof(address.PostalCode), postcodeError.Field.FieldName);

        address.PostalCode = "SW1A 1AA";
        Assert.Empty(NestedDataAnnotationsValidator.ValidateNestedObjects(model));
    }
}
