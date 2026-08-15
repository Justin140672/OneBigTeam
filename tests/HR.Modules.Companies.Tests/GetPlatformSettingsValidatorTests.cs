using HR.Modules.Companies.Features.GetPlatformSettings;

namespace HR.Modules.Companies.Tests;

public class GetPlatformSettingsValidatorTests
{
    [Fact]
    public void Validate_Always_Passes_As_Request_Has_No_Fields()
    {
        var result = new GetPlatformSettingsValidator().Validate(new GetPlatformSettingsRequest());

        Assert.True(result.IsValid);
    }
}
