using HR.Modules.Identity.Features.DisablePlatformAdministrator;

namespace HR.Modules.Identity.Tests;

public class DisablePlatformAdministratorValidatorTests
{
    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new DisablePlatformAdministratorValidator();

        var result = validator.Validate(new DisablePlatformAdministratorRequest(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var validator = new DisablePlatformAdministratorValidator();

        var result = validator.Validate(new DisablePlatformAdministratorRequest(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(DisablePlatformAdministratorRequest.Id));
    }
}
