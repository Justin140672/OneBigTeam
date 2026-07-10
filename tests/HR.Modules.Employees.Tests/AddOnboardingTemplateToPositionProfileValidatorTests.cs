using HR.Modules.Employees.Features.AddOnboardingTemplateToPositionProfile;

namespace HR.Modules.Employees.Tests;

public class AddOnboardingTemplateToPositionProfileValidatorTests
{
    private static readonly AddOnboardingTemplateValidator Validator = new();

    private static AddOnboardingTemplateRequest ValidRequest() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid());

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        Assert.True(Validator.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddOnboardingTemplateRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyPositionProfileId_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { PositionProfileId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddOnboardingTemplateRequest.PositionProfileId));
    }

    [Fact]
    public void Validate_EmptyOnboardingTemplateId_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { OnboardingTemplateId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddOnboardingTemplateRequest.OnboardingTemplateId));
    }
}
