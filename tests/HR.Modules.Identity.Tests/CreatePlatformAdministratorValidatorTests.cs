using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.CreatePlatformAdministrator;

namespace HR.Modules.Identity.Tests;

public class CreatePlatformAdministratorValidatorTests
{
    private static CreatePlatformAdministratorRequest ValidRequest() =>
        new("admin@test.com", PlatformAdministratorRole.SupportStaff);

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new CreatePlatformAdministratorValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Email_Is_Empty()
    {
        var validator = new CreatePlatformAdministratorValidator();
        var request = ValidRequest() with { Email = string.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePlatformAdministratorRequest.Email));
    }

    [Fact]
    public void Validate_Fails_When_Email_Is_Malformed()
    {
        var validator = new CreatePlatformAdministratorValidator();
        var request = ValidRequest() with { Email = "not-an-email" };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePlatformAdministratorRequest.Email));
    }

    [Fact]
    public void Validate_Fails_When_Role_Is_Not_A_Defined_Enum_Value()
    {
        var validator = new CreatePlatformAdministratorValidator();
        var request = ValidRequest() with { Role = (PlatformAdministratorRole)999 };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePlatformAdministratorRequest.Role));
    }
}
