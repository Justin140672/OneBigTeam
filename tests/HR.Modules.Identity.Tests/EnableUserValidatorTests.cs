using HR.Modules.Identity.Features.EnableUser;

namespace HR.Modules.Identity.Tests;

public class EnableUserValidatorTests
{
    private static EnableUserRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new EnableUserValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new EnableUserValidator();
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(EnableUserRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_UserId_Is_Empty()
    {
        var validator = new EnableUserValidator();
        var request = ValidRequest() with { UserId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(EnableUserRequest.UserId));
    }
}
