using HR.Modules.Identity.Features.DisableUser;

namespace HR.Modules.Identity.Tests;

public class DisableUserValidatorTests
{
    private static DisableUserRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new DisableUserValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new DisableUserValidator();
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(DisableUserRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_UserId_Is_Empty()
    {
        var validator = new DisableUserValidator();
        var request = ValidRequest() with { UserId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(DisableUserRequest.UserId));
    }
}
