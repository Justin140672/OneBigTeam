using HR.Modules.Identity.Features.ListPlatformAdministrators;

namespace HR.Modules.Identity.Tests;

public class ListPlatformAdministratorsValidatorTests
{
    [Fact]
    public void Validate_Passes_For_Empty_Request()
    {
        var validator = new ListPlatformAdministratorsValidator();

        var result = validator.Validate(new ListPlatformAdministratorsRequest());

        Assert.True(result.IsValid);
    }
}
