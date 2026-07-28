using HR.Modules.Identity.Features.CancelInvite;

namespace HR.Modules.Identity.Tests;

public class CancelInviteValidatorTests
{
    private static CancelInviteRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        InviteId = Guid.NewGuid(),
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new CancelInviteValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new CancelInviteValidator();
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CancelInviteRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_InviteId_Is_Empty()
    {
        var validator = new CancelInviteValidator();
        var request = ValidRequest() with { InviteId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CancelInviteRequest.InviteId));
    }
}
