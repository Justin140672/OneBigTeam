using HR.Modules.Employees.Features.ListOnboardingTemplates;

namespace HR.Modules.Employees.Tests;

public class ListOnboardingTemplatesValidatorTests
{
    private readonly ListOnboardingTemplatesValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_For_Valid_Request()
    {
        var result = _validator.Validate(new ListOnboardingTemplatesRequest { CompanyId = Guid.NewGuid() });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new ListOnboardingTemplatesRequest { CompanyId = Guid.Empty });

        Assert.False(result.IsValid);
    }
}
