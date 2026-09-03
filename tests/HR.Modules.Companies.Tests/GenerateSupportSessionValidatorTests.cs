using HR.Modules.Companies.Features.GenerateSupportSession;

namespace HR.Modules.Companies.Tests;

public class GenerateSupportSessionValidatorTests
{
    private static GenerateSupportSessionRequest ValidRequest() => new(
        Guid.NewGuid(),
        "Investigating a customer-reported issue with compensation export.");

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = new GenerateSupportSessionValidator().Validate(ValidRequest());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = new GenerateSupportSessionValidator().Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GenerateSupportSessionRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Empty()
    {
        var result = new GenerateSupportSessionValidator().Validate(ValidRequest() with { Reason = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GenerateSupportSessionRequest.Reason));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Too_Short()
    {
        var result = new GenerateSupportSessionValidator().Validate(ValidRequest() with { Reason = "short" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GenerateSupportSessionRequest.Reason));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Exceeds_1000_Characters()
    {
        var result = new GenerateSupportSessionValidator().Validate(ValidRequest() with { Reason = new string('A', 1001) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GenerateSupportSessionRequest.Reason));
    }
}
