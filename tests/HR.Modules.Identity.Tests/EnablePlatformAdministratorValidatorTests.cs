using HR.Modules.Identity.Features.EnablePlatformAdministrator;

namespace HR.Modules.Identity.Tests;

public class EnablePlatformAdministratorValidatorTests
{
    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new EnablePlatformAdministratorValidator();

        var result = validator.Validate(new EnablePlatformAdministratorRequest(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var validator = new EnablePlatformAdministratorValidator();

        var result = validator.Validate(new EnablePlatformAdministratorRequest(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(EnablePlatformAdministratorRequest.Id));
    }
}
