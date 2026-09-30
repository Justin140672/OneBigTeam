using HR.Modules.Employees.Features.SetDefaultOnboardingTemplate;

namespace HR.Modules.Employees.Tests;

public class SetDefaultOnboardingTemplateValidatorTests
{
    private readonly SetDefaultOnboardingTemplateValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new SetDefaultOnboardingTemplateRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid()
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new SetDefaultOnboardingTemplateRequest { Id = Guid.NewGuid() });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SetDefaultOnboardingTemplateRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var result = _validator.Validate(new SetDefaultOnboardingTemplateRequest { CompanyId = Guid.NewGuid() });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SetDefaultOnboardingTemplateRequest.Id));
    }
}
