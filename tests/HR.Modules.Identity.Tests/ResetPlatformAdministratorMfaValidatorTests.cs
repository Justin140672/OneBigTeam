using HR.Modules.Identity.Features.ResetPlatformAdministratorMfa;

namespace HR.Modules.Identity.Tests;

public class ResetPlatformAdministratorMfaValidatorTests
{
    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new ResetPlatformAdministratorMfaValidator();

        var result = validator.Validate(new ResetPlatformAdministratorMfaRequest(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var validator = new ResetPlatformAdministratorMfaValidator();

        var result = validator.Validate(new ResetPlatformAdministratorMfaRequest(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResetPlatformAdministratorMfaRequest.Id));
    }
}
