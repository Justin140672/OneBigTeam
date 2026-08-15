using HR.Modules.Companies.Features.RedeemSupportSession;

namespace HR.Modules.Companies.Tests;

public class RedeemSupportSessionValidatorTests
{
    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = new RedeemSupportSessionValidator().Validate(new RedeemSupportSessionRequest("some-token-value"));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Token_Is_Empty()
    {
        var result = new RedeemSupportSessionValidator().Validate(new RedeemSupportSessionRequest(string.Empty));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RedeemSupportSessionRequest.Token));
    }
}
