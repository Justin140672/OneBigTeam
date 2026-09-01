using HR.Modules.Companies.Features.PlaceCompanyLegalHold;

namespace HR.Modules.Companies.Tests;

public class PlaceCompanyLegalHoldValidatorTests
{
    private static readonly PlaceCompanyLegalHoldValidator Validator = new();

    private static PlaceCompanyLegalHoldRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        Reason = "Litigation hold placed for case reference 1234.",
    };

    [Fact]
    public void Passes_For_Valid_Request()
    {
        Assert.True(Validator.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Fails_When_CompanyId_Is_Empty()
    {
        var result = Validator.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PlaceCompanyLegalHoldRequest.CompanyId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Fails_When_Reason_Is_Empty_Or_Whitespace(string reason)
    {
        var result = Validator.Validate(ValidRequest() with { Reason = reason });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PlaceCompanyLegalHoldRequest.Reason));
    }

    [Fact]
    public void Fails_When_Reason_Is_Shorter_Than_5_Characters()
    {
        var result = Validator.Validate(ValidRequest() with { Reason = "abcd" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PlaceCompanyLegalHoldRequest.Reason));
    }

    [Fact]
    public void Passes_When_Reason_Is_Exactly_5_Characters()
    {
        Assert.True(Validator.Validate(ValidRequest() with { Reason = "abcde" }).IsValid);
    }

    [Fact]
    public void Passes_When_Reason_Is_Exactly_1000_Characters()
    {
        Assert.True(Validator.Validate(ValidRequest() with { Reason = new string('A', 1000) }).IsValid);
    }

    [Fact]
    public void Fails_When_Reason_Exceeds_1000_Characters()
    {
        var result = Validator.Validate(ValidRequest() with { Reason = new string('A', 1001) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PlaceCompanyLegalHoldRequest.Reason));
    }
}
