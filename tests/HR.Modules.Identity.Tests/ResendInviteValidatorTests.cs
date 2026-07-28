using HR.Modules.Identity.Features.ResendInvite;

namespace HR.Modules.Identity.Tests;

public class ResendInviteValidatorTests
{
    private static ResendInviteRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        InviteId = Guid.NewGuid(),
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new ResendInviteValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new ResendInviteValidator();
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResendInviteRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_InviteId_Is_Empty()
    {
        var validator = new ResendInviteValidator();
        var request = ValidRequest() with { InviteId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResendInviteRequest.InviteId));
    }
}
