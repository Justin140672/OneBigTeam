using HR.Modules.Companies.Features.RevokeSupportSession;

namespace HR.Modules.Companies.Tests;

public class RevokeSupportSessionValidatorTests
{
    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = new RevokeSupportSessionValidator().Validate(new RevokeSupportSessionRequest(Guid.NewGuid()));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_SupportSessionId_Is_Empty()
    {
        var result = new RevokeSupportSessionValidator().Validate(new RevokeSupportSessionRequest(Guid.Empty));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RevokeSupportSessionRequest.SupportSessionId));
    }
}
