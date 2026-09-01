using HR.Modules.Companies.Features.LiftCompanyLegalHold;

namespace HR.Modules.Companies.Tests;

public class LiftCompanyLegalHoldValidatorTests
{
    private static readonly LiftCompanyLegalHoldValidator Validator = new();

    private static LiftCompanyLegalHoldRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        Reason = "Matter resolved; lifting the legal hold.",
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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LiftCompanyLegalHoldRequest.CompanyId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Fails_When_Reason_Is_Empty_Or_Whitespace(string reason)
    {
        var result = Validator.Validate(ValidRequest() with { Reason = reason });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LiftCompanyLegalHoldRequest.Reason));
    }

    [Fact]
    public void Fails_When_Reason_Is_Shorter_Than_5_Characters()
    {
        var result = Validator.Validate(ValidRequest() with { Reason = "abcd" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LiftCompanyLegalHoldRequest.Reason));
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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LiftCompanyLegalHoldRequest.Reason));
    }
}
