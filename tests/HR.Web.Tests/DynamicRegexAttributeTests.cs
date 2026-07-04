using System.ComponentModel.DataAnnotations;
using HR.Web.Models;

namespace HR.Web.Tests;

public class DynamicRegexAttributeTests
{
    private const string UkPostcodeRegex = @"^[A-Za-z]{1,2}\d[A-Za-z\d]?\s?\d[A-Za-z]{2}$";
    private const string UkMobileRegex = @"^(?:\+44\s?|0)7\d{3}(?:\s?\d{3}){2}$";
    private const string UkTelephoneRegex = @"^(?:\+44\s?|0)(?:\d\s?){9,10}$";

    private static IList<ValidationResult> Validate(object model, string memberName)
    {
        var context = new ValidationContext(model) { MemberName = memberName };
        var results = new List<ValidationResult>();
        var value = model.GetType().GetProperty(memberName)!.GetValue(model);
        Validator.TryValidateProperty(value, context, results);
        return results;
    }

    private sealed class SinglePatternModel
    {
        [DynamicRegex(nameof(PostcodeRegexPattern), ErrorMessage = "Enter a valid postcode.")]
        public string? PostCode { get; set; }
        public string? PostcodeRegexPattern { get; set; }
    }

    private sealed class DualPatternModel
    {
        [DynamicRegex(nameof(MobileRegexPattern), nameof(TelephoneRegexPattern), ErrorMessage = "Enter a valid phone number.")]
        public string? PhoneNumber { get; set; }
        public string? MobileRegexPattern { get; set; }
        public string? TelephoneRegexPattern { get; set; }
    }

    [Fact]
    public void IsValid_When_Value_Is_Null()
    {
        var model = new SinglePatternModel { PostCode = null, PostcodeRegexPattern = UkPostcodeRegex };
        Assert.Empty(Validate(model, nameof(SinglePatternModel.PostCode)));
    }

    [Fact]
    public void IsValid_When_Value_Is_Empty_Or_Whitespace()
    {
        var model = new SinglePatternModel { PostCode = "   ", PostcodeRegexPattern = UkPostcodeRegex };
        Assert.Empty(Validate(model, nameof(SinglePatternModel.PostCode)));
    }

    [Fact]
    public void IsValid_When_Pattern_Property_Is_Not_Set()
    {
        // No pattern fetched yet (e.g. component still loading company settings) — must not
        // block the user with a false-positive error.
        var model = new SinglePatternModel { PostCode = "not a postcode", PostcodeRegexPattern = null };
        Assert.Empty(Validate(model, nameof(SinglePatternModel.PostCode)));
    }

    [Fact]
    public void IsValid_When_Value_Matches_Configured_Pattern()
    {
        var model = new SinglePatternModel { PostCode = "SW1A 1AA", PostcodeRegexPattern = UkPostcodeRegex };
        Assert.Empty(Validate(model, nameof(SinglePatternModel.PostCode)));
    }

    [Fact]
    public void IsValid_Ignores_Case()
    {
        var model = new SinglePatternModel { PostCode = "sw1a 1aa", PostcodeRegexPattern = UkPostcodeRegex };
        Assert.Empty(Validate(model, nameof(SinglePatternModel.PostCode)));
    }

    [Fact]
    public void Invalid_When_Value_Does_Not_Match_Configured_Pattern()
    {
        var model = new SinglePatternModel { PostCode = "not a postcode", PostcodeRegexPattern = UkPostcodeRegex };
        var results = Validate(model, nameof(SinglePatternModel.PostCode));

        var result = Assert.Single(results);
        Assert.Equal("Enter a valid postcode.", result.ErrorMessage);
        Assert.Contains(nameof(SinglePatternModel.PostCode), result.MemberNames);
    }

    [Theory]
    [InlineData("07700 900000")] // matches mobile pattern
    [InlineData("01234 567890")] // matches telephone pattern
    public void IsValid_When_Value_Matches_Any_Of_Multiple_Patterns(string phoneNumber)
    {
        var model = new DualPatternModel
        {
            PhoneNumber = phoneNumber,
            MobileRegexPattern = UkMobileRegex,
            TelephoneRegexPattern = UkTelephoneRegex
        };

        Assert.Empty(Validate(model, nameof(DualPatternModel.PhoneNumber)));
    }

    [Fact]
    public void Invalid_When_Value_Matches_Neither_Of_Multiple_Patterns()
    {
        var model = new DualPatternModel
        {
            PhoneNumber = "not-a-phone-number",
            MobileRegexPattern = UkMobileRegex,
            TelephoneRegexPattern = UkTelephoneRegex
        };

        var results = Validate(model, nameof(DualPatternModel.PhoneNumber));

        var result = Assert.Single(results);
        Assert.Equal("Enter a valid phone number.", result.ErrorMessage);
    }

    [Fact]
    public void IsValid_When_Only_One_Of_Multiple_Pattern_Properties_Is_Set()
    {
        var model = new DualPatternModel
        {
            PhoneNumber = "01234 567890",
            MobileRegexPattern = null,
            TelephoneRegexPattern = UkTelephoneRegex
        };

        Assert.Empty(Validate(model, nameof(DualPatternModel.PhoneNumber)));
    }
}
