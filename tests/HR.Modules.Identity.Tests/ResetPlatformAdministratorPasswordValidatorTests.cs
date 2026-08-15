using HR.Modules.Identity.Features.ResetPlatformAdministratorPassword;

namespace HR.Modules.Identity.Tests;

public class ResetPlatformAdministratorPasswordValidatorTests
{
    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new ResetPlatformAdministratorPasswordValidator();

        var result = validator.Validate(new ResetPlatformAdministratorPasswordRequest(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var validator = new ResetPlatformAdministratorPasswordValidator();

        var result = validator.Validate(new ResetPlatformAdministratorPasswordRequest(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResetPlatformAdministratorPasswordRequest.Id));
    }
}
